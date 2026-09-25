"""
Oracle test vectors for the Varint Calculator.

Nothing in here uses the calculator's own code. Every expected value comes from a
reference implementation:

  * SQLite (Python's sqlite3 module) writes real database files. The generator reads
    the raw bytes SQLite wrote: rowid and payload-size varints, record-header serial
    types, and the split of each cell's payload between the b-tree page and its
    overflow chain. The split is measured physically: the local bytes must match the
    start of the record, and the overflow chain must hold exactly the rest.
  * Google's protobuf runtime (the `protobuf` package, upb backend) serializes and
    parses uint64 / int64 / sint64 / bool fields, and packs field tags.

Run it from anywhere; it writes JSON into VarIntCalculator.Tests/Vectors:

    python tools/oracle/make_vectors.py

The vectors are committed, so the C# tests do not need Python to run.
"""
import json
import os
import random
import sqlite3
import struct
import sys
import tempfile

from google.protobuf import descriptor_pb2, descriptor_pool, message_factory
from google.protobuf.internal import api_implementation, wire_format
import google.protobuf

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "VarIntCalculator.Tests", "Vectors")
RNG = random.Random(20260925)

U64 = (1 << 64) - 1


# ---------------------------------------------------------------------------
# Small readers used only to FIND structures in the file. Each value they read is
# cross-checked against what was inserted, so a harness bug cannot pass silently.
# ---------------------------------------------------------------------------

def read_sqlite_varint(buf, off):
    """Returns (value, length). Only used to locate varints SQLite wrote."""
    v = 0
    for i in range(9):
        b = buf[off + i]
        if i == 8:
            return ((v << 8) | b) & U64, 9
        v = (v << 7) | (b & 0x7F)
        if b < 0x80:
            return v, i + 1
    raise AssertionError("unreachable")


def to_signed64(v):
    return v - (1 << 64) if v >= (1 << 63) else v


def u32(buf, off):
    return struct.unpack_from(">I", buf, off)[0]


def u16(buf, off):
    return struct.unpack_from(">H", buf, off)[0]


class DbFile:
    def __init__(self, path):
        with open(path, "rb") as f:
            self.data = f.read()
        self.page_size = u16(self.data, 16)
        if self.page_size == 1:
            self.page_size = 65536
        self.reserved = self.data[20]
        self.usable = self.page_size - self.reserved
        self.page_count = len(self.data) // self.page_size

    def page(self, pgno):
        start = (pgno - 1) * self.page_size
        return start, self.data[start:start + self.page_size]

    def btree_pages(self):
        """Yields (pgno, page_type, header_offset) for every b-tree page."""
        for pgno in range(1, self.page_count + 1):
            _, page = self.page(pgno)
            hdr = 100 if pgno == 1 else 0
            t = page[hdr]
            if t in (0x02, 0x05, 0x0A, 0x0D):
                yield pgno, t, hdr

    def cells(self, pgno, hdr):
        start, page = self.page(pgno)
        t = page[hdr]
        n = u16(page, hdr + 3)
        ptr_base = hdr + (12 if t in (0x02, 0x05) else 8)
        for i in range(n):
            yield start + u16(page, ptr_base + 2 * i)


def measure_split(db, payload_start, P, expected):
    """
    Physically measures how many payload bytes sit on the b-tree page.
    `expected` is the full record. Returns (local, overflow_pages).
    """
    data = db.data
    if len(expected) != P:
        raise AssertionError(f"expected record is {len(expected)} bytes, P is {P}")
    # The local bytes match the record; the first mismatch is where the 4-byte
    # overflow page number begins (unless random bytes happen to agree, handled below).
    limit = min(P, db.usable)
    i = 0
    while i < limit and data[payload_start + i] == expected[i]:
        i += 1
    if i == P:
        return P, 0
    for local in range(i, max(-1, i - 5), -1):
        pages = walk_chain(db, payload_start + local, expected, local, P)
        if pages is not None:
            return local, pages
    raise AssertionError("could not reconstruct the overflow chain")


def walk_chain(db, ptr_off, expected, local, P):
    data = db.data
    pgno = u32(data, ptr_off)
    got = bytearray()
    pages = 0
    seen = set()
    per_page = db.usable - 4
    while pgno != 0:
        if pgno < 2 or pgno > db.page_count or pgno in seen:
            return None
        seen.add(pgno)
        start = (pgno - 1) * db.page_size
        nxt = u32(data, start)
        take = min(per_page, P - local - len(got))
        got += data[start + 4:start + 4 + take]
        pages += 1
        pgno = nxt
        if len(got) >= P - local and pgno != 0:
            return None
    if bytes(got) != bytes(expected[local:]):
        return None
    return pages


def sqlite_varint_encode_reference(v):
    """Only used to size the blob for a target P; the vectors hold SQLite's bytes."""
    v &= U64
    if v >> 56:
        out = [v & 0xFF]
        v >>= 8
        for _ in range(8):
            out.append((v & 0x7F) | 0x80)
            v >>= 7
        return bytes(reversed(out))
    out = [v & 0x7F]
    v >>= 7
    while v:
        out.append((v & 0x7F) | 0x80)
        v >>= 7
    return bytes(reversed(out))


def new_db(path, page_size, reserved):
    if os.path.exists(path):
        os.remove(path)
    con = sqlite3.connect(path)
    con.execute("PRAGMA journal_mode=DELETE")
    con.execute(f"PRAGMA page_size={page_size}")
    con.execute("PRAGMA user_version=1")  # writes page 1 at this page size
    con.commit()
    con.close()
    if reserved:
        with open(path, "r+b") as f:
            f.seek(20)
            f.write(bytes([reserved]))
            # Page 1 holds an empty sqlite_schema b-tree: its cell content area must
            # start inside the usable size, or SQLite reports the page as corrupt.
            f.seek(100 + 5)
            f.write(struct.pack(">H", page_size - reserved))
    con = sqlite3.connect(path)
    con.execute("PRAGMA journal_mode=DELETE")
    return con


# ---------------------------------------------------------------------------
# SQLite: varints (rowids) and serial types
# ---------------------------------------------------------------------------

def sqlite_varint_vectors(tmp):
    values = set()
    for k in range(0, 64):
        for d in (-1, 0, 1):
            x = (1 << k) + d
            if 0 <= x < (1 << 63):
                values.add(x)
    for k in range(1, 10):
        values.add((1 << (7 * k)) - 1)
        values.add(1 << (7 * k))
    values.update([(1 << 63) - 1, 0, 1, 127, 128, 240, 2287, 2288, 67823, 67824])
    for _ in range(300):
        bits = RNG.randint(1, 63)
        values.add(RNG.getrandbits(bits))
    negatives = {-1, -2, -127, -128, -129, -(1 << 31), -(1 << 62), -(1 << 63)}
    for _ in range(60):
        negatives.add(-RNG.randint(1, (1 << 63)))
    values = {v for v in values if 0 <= v < (1 << 63)}  # rowids are signed 64-bit
    rowids = sorted(values | negatives)

    path = os.path.join(tmp, "rowids.db")
    con = new_db(path, 4096, 0)
    con.execute("CREATE TABLE t(a)")
    for r in rowids:
        con.execute("INSERT INTO t(rowid, a) VALUES (?, NULL)", (r,))
    con.commit()
    con.execute("PRAGMA integrity_check").fetchone()
    con.close()

    db = DbFile(path)
    found = {}
    payload_sizes = {}
    for pgno, t, hdr in db.btree_pages():
        if t != 0x0D or pgno == 1:
            continue
        for cell in db.cells(pgno, hdr):
            p, plen = read_sqlite_varint(db.data, cell)
            payload_sizes[p] = db.data[cell:cell + plen]
            r, rlen = read_sqlite_varint(db.data, cell + plen)
            found[to_signed64(r)] = db.data[cell + plen:cell + plen + rlen]
    missing = [r for r in rowids if r not in found]
    if missing:
        raise AssertionError(f"rowids not found in file: {missing[:5]}")

    vectors = []
    for r in rowids:
        raw = found[r]
        vectors.append({
            "hex": raw.hex().upper(),
            "unsigned": str(r & U64),
            "signed": str(r),
            "source": "rowid",
        })
    for p, raw in sorted(payload_sizes.items()):
        vectors.append({"hex": raw.hex().upper(), "unsigned": str(p), "signed": str(p), "source": "payload size"})
    return vectors


def payload_size_varint_vectors():
    return [{"hex": raw.hex().upper(), "unsigned": str(p), "signed": str(p), "source": "payload size"}
            for p, raw in sorted(P_VARINTS.items())]


def sqlite_serial_type_vectors(tmp):
    path = os.path.join(tmp, "serial.db")
    con = new_db(path, 4096, 0)
    con.execute("CREATE TABLE t(v)")
    samples = [
        None, 0, 1, 2, -1, 127, -128, 128, -129, 32767, -32768, 32768, 8388607, -8388608,
        8388608, 2147483647, -2147483648, 2147483648, 140737488355327, -140737488355328,
        140737488355328, 9223372036854775807, -9223372036854775808, 3.5, -0.0, 1e300,
        "", "a", "abc", "x" * 57, "x" * 58, "x" * 500, "éé",
        b"", b"\x00", b"\x01\x02", b"\xff" * 100, b"z" * 3000,
    ]
    for s in samples:
        con.execute("INSERT INTO t(v) VALUES (?)", (s,))
    con.commit()
    con.close()

    db = DbFile(path)
    vectors = []
    seen = set()
    for pgno, t, hdr in db.btree_pages():
        if t != 0x0D or pgno == 1:
            continue
        for cell in db.cells(pgno, hdr):
            p, plen = read_sqlite_varint(db.data, cell)
            _, rlen = read_sqlite_varint(db.data, cell + plen)
            rec = cell + plen + rlen
            hsize, hlen = read_sqlite_varint(db.data, rec)
            off = rec + hlen
            types = []
            while off < rec + hsize:
                st, stlen = read_sqlite_varint(db.data, off)
                types.append(st)
                off += stlen
            # One column: the body is everything after the header.
            body = p - hsize
            st = types[0]
            if st in seen:
                continue
            seen.add(st)
            vectors.append({"serialType": str(st), "contentBytes": body})
    return vectors


# ---------------------------------------------------------------------------
# SQLite: local payload and overflow
# ---------------------------------------------------------------------------

P_VARINTS = {}


def blob_len_for_payload(P, extra):
    """Blob length n so the record (blob, then NULL or 5) is exactly P bytes, or None."""
    for L in range(1, 6):
        n = P - 2 - L - extra  # header = size byte + blob type (L) + one more type byte
        if n < 0:
            continue
        if len(sqlite_varint_encode_reference(12 + 2 * n)) == L:
            return n
    return None


def payload_targets(usable, kind):
    X = usable - 35 if kind == "table" else (usable - 12) * 64 // 255 - 23
    M = (usable - 12) * 32 // 255 - 23
    per = usable - 4
    t = {1, 10, 100, X - 1, X, X + 1, X + 2, M, M + 1}
    for k in range(0, 4):
        base = M + k * per
        for d in (-2, -1, 0, 1, 2, X - M - 1, X - M, X - M + 1, X - M + 2, per - 1):
            t.add(base + d)
    for _ in range(14):
        t.add(RNG.randint(X + 1, 5 * usable))
    t.add(10 * usable + 7)
    return sorted(p for p in t if 70 <= p <= 12 * usable)


def table_payload_vectors(tmp, page_size, reserved):
    usable = page_size - reserved
    path = os.path.join(tmp, f"table_{page_size}_{reserved}.db")
    con = new_db(path, page_size, reserved)
    con.execute("CREATE TABLE t(a, b)")
    rows = {}
    rowid = 0
    for P in payload_targets(usable, "table"):
        for extra in (0, 1):
            n = blob_len_for_payload(P, extra)
            if n is None:
                continue
            rowid += 1
            blob = RNG.randbytes(n)
            rows[rowid] = (blob, b"" if extra == 0 else bytes([5]))
            con.execute("INSERT INTO t(rowid, a, b) VALUES (?, ?, ?)",
                        (rowid, blob, None if extra == 0 else 5))
            break
    con.commit()
    ok = con.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    if ok != "ok":
        raise AssertionError(f"integrity_check: {ok}")

    db = DbFile(path)
    if db.usable != usable:
        raise AssertionError("reserved bytes did not take")
    vectors = []
    for pgno, t, hdr in db.btree_pages():
        if t != 0x0D or pgno == 1:
            continue
        for cell in db.cells(pgno, hdr):
            P, plen = read_sqlite_varint(db.data, cell)
            P_VARINTS.setdefault(P, db.data[cell:cell + plen])
            r, rlen = read_sqlite_varint(db.data, cell + plen)
            start = cell + plen + rlen
            blob, tail = rows[r]
            hsize, _ = read_sqlite_varint(db.data, start)
            header = db.data[start:start + hsize]  # the header always fits locally (M >= 35)
            expected = header + blob + tail
            local, pages = measure_split(db, start, P, expected)
            vectors.append({"pageSize": page_size, "reserved": reserved, "kind": "table",
                            "payload": P, "local": local, "overflowPages": pages})
    return vectors


def int_bytes_for_serial(v, st):
    size = {1: 1, 2: 2, 3: 3, 4: 4, 5: 6, 6: 8, 8: 0, 9: 0}[st]
    return (v & ((1 << (8 * size)) - 1)).to_bytes(size, "big") if size else b""


def index_payload_vectors(tmp, page_size, reserved):
    usable = page_size - reserved
    path = os.path.join(tmp, f"index_{page_size}_{reserved}.db")
    con = new_db(path, page_size, reserved)
    con.execute("CREATE TABLE u(x)")
    con.execute("CREATE INDEX ux ON u(x)")
    blobs = {}
    rowid = 1000  # 2-byte rowid body (serial type 2) keeps the arithmetic uniform
    for P in payload_targets(usable, "index"):
        # record = [hdrsize][blob st][rowid st=2] + blob + 2 rowid bytes
        n = None
        for L in range(1, 6):
            cand = P - 1 - L - 1 - 2
            if cand >= 0 and len(sqlite_varint_encode_reference(12 + 2 * cand)) == L:
                n = cand
                break
        if n is None:
            continue
        rowid += 1
        blob = RNG.randbytes(n)
        blobs[bytes(blob[:16])] = (rowid, blob)
        con.execute("INSERT INTO u(rowid, x) VALUES (?, ?)", (rowid, blob))
    con.commit()
    ok = con.execute("PRAGMA integrity_check").fetchone()[0]
    con.close()
    if ok != "ok":
        raise AssertionError(f"integrity_check: {ok}")

    db = DbFile(path)
    vectors = []
    for pgno, t, hdr in db.btree_pages():
        if t not in (0x0A, 0x02):
            continue
        for cell in db.cells(pgno, hdr):
            off = cell + (4 if t == 0x02 else 0)
            P, plen = read_sqlite_varint(db.data, off)
            start = off + plen
            hsize, hlen = read_sqlite_varint(db.data, start)
            header = db.data[start:start + hsize]
            st_blob, l1 = read_sqlite_varint(db.data, start + hlen)
            st_rowid, _ = read_sqlite_varint(db.data, start + hlen + l1)
            n = (st_blob - 12) // 2
            key = bytes(db.data[start + hsize:start + hsize + 16])
            if key not in blobs:
                continue
            rid, blob = blobs[key]
            if len(blob) != n:
                raise AssertionError("blob length mismatch")
            expected = header + blob + int_bytes_for_serial(rid, st_rowid)
            local, pages = measure_split(db, start, P, expected)
            vectors.append({"pageSize": page_size, "reserved": reserved,
                            "kind": "index", "interior": t == 0x02,
                            "payload": P, "local": local, "overflowPages": pages})
    return vectors


# ---------------------------------------------------------------------------
# Protobuf
# ---------------------------------------------------------------------------

def protobuf_types():
    fdp = descriptor_pb2.FileDescriptorProto()
    fdp.name = "varint_oracle.proto"
    fdp.package = "oracle"
    fdp.syntax = "proto2"
    m = fdp.message_type.add()
    m.name = "V"
    T = descriptor_pb2.FieldDescriptorProto
    for num, (name, typ) in enumerate([
            ("u64", T.TYPE_UINT64), ("i64", T.TYPE_INT64), ("s64", T.TYPE_SINT64),
            ("b", T.TYPE_BOOL), ("i32", T.TYPE_INT32)], start=1):
        f = m.field.add()
        f.name = name
        f.number = num
        f.type = typ
        f.label = T.LABEL_OPTIONAL
    pool = descriptor_pool.DescriptorPool()
    pool.Add(fdp)
    return message_factory.GetMessageClass(pool.FindMessageTypeByName("oracle.V"))


def protobuf_vectors():
    V = protobuf_types()

    def field_bytes(msg, tag):
        raw = msg.SerializeToString()
        if not raw:
            return b"\x00"  # proto3 skips a default value; its varint is a single 00
        if raw[0] != tag:
            raise AssertionError("unexpected tag")
        return raw[1:]

    encode = []
    values = {0, 1, 127, 128, 150, 300, U64, (1 << 63) - 1, 1 << 63}
    for k in range(1, 10):
        values.update([(1 << (7 * k)) - 1, 1 << (7 * k)])
    for _ in range(200):
        values.add(RNG.getrandbits(RNG.randint(1, 64)))
    for v in sorted(values):
        m = V(); m.u64 = v
        encode.append({"kind": "uint64", "value": str(v), "hex": field_bytes(m, 0x08).hex().upper()})
    signed = {0, 1, -1, -2, 63, -64, 64, -65, 150, -150, (1 << 63) - 1, -(1 << 63), -(1 << 31)}
    for _ in range(120):
        signed.add(RNG.randint(-(1 << 63), (1 << 63) - 1))
    for v in sorted(signed):
        m = V(); m.i64 = v
        encode.append({"kind": "int64", "value": str(v), "hex": field_bytes(m, 0x10).hex().upper()})
        m = V(); m.s64 = v
        encode.append({"kind": "sint64", "value": str(v), "hex": field_bytes(m, 0x18).hex().upper()})

    decode = []

    def parse(field_tag, attr, raw):
        m = V()
        try:
            m.ParseFromString(bytes([field_tag]) + raw)
        except Exception:
            return None
        return getattr(m, attr)

    samples = []
    for e in encode:
        samples.append(bytes.fromhex(e["hex"]))
    samples += [
        bytes([0x81, 0x00]), bytes([0x80, 0x80, 0x00]), bytes([0xFF] * 9 + [0x01]),
        bytes([0xFF] * 9 + [0x7F]), bytes([0x80] * 9 + [0x02]), bytes([0x80] * 9 + [0x00]),
        bytes([0xFF] * 10 + [0x01]), bytes([0x81]), bytes([0xFF] * 10), bytes([0x80] * 9),
    ]
    for _ in range(150):
        n = RNG.randint(1, 10)
        raw = bytes([RNG.randint(0x80, 0xFF) for _ in range(n - 1)] + [RNG.randint(0, 0x7F)])
        samples.append(raw)
    seen = set()
    for raw in samples:
        if raw in seen:
            continue
        seen.add(raw)
        u = parse(0x08, "u64", raw)
        decode.append({
            "hex": raw.hex().upper(),
            "valid": u is not None,
            "uint64": None if u is None else str(u),
            "int64": None if u is None else str(parse(0x10, "i64", raw)),
            "sint64": None if u is None else str(parse(0x18, "s64", raw)),
            "int32": None if u is None else str(parse(0x28, "i32", raw)),
        })

    tags = []
    for field in [1, 2, 15, 16, 2047, 2048, 19000, 262143, (1 << 29) - 1]:
        for wt in range(6):
            tags.append({"field": field, "wireType": wt, "tag": str(wire_format.PackTag(field, wt))})
    return encode, decode, tags


def main():
    os.makedirs(OUT, exist_ok=True)
    meta = {
        "generator": "tools/oracle/make_vectors.py",
        "sqlite": sqlite3.sqlite_version,
        "protobuf": f"{google.protobuf.__version__} ({api_implementation.Type()})",
        "python": sys.version.split()[0],
    }
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        sv = sqlite_varint_vectors(tmp)
        st = sqlite_serial_type_vectors(tmp)
        payload = []
        combos = [(512, 0), (512, 32), (1024, 0), (1024, 12), (2048, 0), (4096, 0), (4096, 48),
                  (4096, 80), (8192, 0), (16384, 0), (32768, 0), (65536, 0), (65536, 255)]
        for ps, rs in combos:
            payload += table_payload_vectors(tmp, ps, rs)
            payload += index_payload_vectors(tmp, ps, rs)
    sv += payload_size_varint_vectors()
    pe, pd, pt = protobuf_vectors()

    def dump(name, rows):
        with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump({"meta": meta, "vectors": rows}, f, indent=1)
            f.write("\n")
        print(f"{name}: {len(rows)} vectors")

    dump("sqlite_varints.json", sv)
    dump("sqlite_serial_types.json", st)
    dump("sqlite_payload.json", payload)
    dump("protobuf_encode.json", pe)
    dump("protobuf_decode.json", pd)
    dump("protobuf_tags.json", pt)


if __name__ == "__main__":
    main()
