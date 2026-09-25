using System;

namespace VarIntCalculator.Core
{
    public enum SerialKind
    {
        Null,
        Integer,
        Float,
        Zero,
        One,
        Reserved,
        Blob,
        Text,
    }

    /// <summary>
    /// A varint read as a record-header serial type
    /// (https://www.sqlite.org/fileformat2.html#record_format).
    /// </summary>
    public readonly record struct SerialType(ulong Code, SerialKind Kind, ulong ContentBytes)
    {
        /// <summary>
        /// No SQLite build stores a TEXT or BLOB, or a row, of 2^31 bytes or more
        /// (https://www.sqlite.org/limits.html#max_length).
        /// </summary>
        public const ulong SqliteLengthLimit = int.MaxValue;

        public static SerialType From(ulong code) => code switch
        {
            0 => new SerialType(code, SerialKind.Null, 0),
            1 => new SerialType(code, SerialKind.Integer, 1),
            2 => new SerialType(code, SerialKind.Integer, 2),
            3 => new SerialType(code, SerialKind.Integer, 3),
            4 => new SerialType(code, SerialKind.Integer, 4),
            5 => new SerialType(code, SerialKind.Integer, 6),
            6 => new SerialType(code, SerialKind.Integer, 8),
            7 => new SerialType(code, SerialKind.Float, 8),
            8 => new SerialType(code, SerialKind.Zero, 0),
            9 => new SerialType(code, SerialKind.One, 0),
            10 or 11 => new SerialType(code, SerialKind.Reserved, 0),
            _ when code % 2 == 0 => new SerialType(code, SerialKind.Blob, (code - 12) / 2),
            _ => new SerialType(code, SerialKind.Text, (code - 13) / 2),
        };

        /// <summary>Content size in bytes; null for the reserved codes 10 and 11, whose size is not defined.</summary>
        public ulong? KnownContentBytes => Kind == SerialKind.Reserved ? null : ContentBytes;

        public bool IsLongerThanSqliteAllows =>
            Kind is SerialKind.Blob or SerialKind.Text && ContentBytes > SqliteLengthLimit;

        /// <summary>The serial type SQLite writes for a TEXT or BLOB of this many bytes.</summary>
        public static ulong ForText(ulong bytes) => checked(13 + 2 * bytes);

        public static ulong ForBlob(ulong bytes) => checked(12 + 2 * bytes);
    }

    public enum BTreeKind
    {
        /// <summary>A cell on a table b-tree leaf page (page type 0x0D).</summary>
        TableLeaf,

        /// <summary>A cell on an index b-tree page, leaf or interior (page types 0x0A and 0x02).</summary>
        Index,
    }

    /// <summary>
    /// The page parameters the local-payload arithmetic depends on
    /// (https://www.sqlite.org/fileformat2.html#b_tree_pages).
    /// </summary>
    public readonly record struct PageGeometry(int PageSize, int ReservedBytes, BTreeKind Kind)
    {
        public const int DefaultPageSize = 4096;

        /// <summary>SQLite refuses a database whose usable page size is under 480 bytes.</summary>
        public const int MinUsableSize = 480;

        public static readonly int[] PageSizes = { 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 };

        public static PageGeometry Default => new(DefaultPageSize, 0, BTreeKind.TableLeaf);

        /// <summary>U: the page size less the reserved bytes at the end of every page.</summary>
        public int UsableSize => PageSize - ReservedBytes;

        /// <summary>X: the most payload a cell keeps on the b-tree page before spilling.</summary>
        public int MaxLocal => Kind == BTreeKind.TableLeaf
            ? UsableSize - 35
            : (UsableSize - 12) * 64 / 255 - 23;

        /// <summary>M: the least payload a spilling cell keeps on the b-tree page.</summary>
        public int MinLocal => (UsableSize - 12) * 32 / 255 - 23;

        /// <summary>Content bytes one overflow page holds after its 4-byte next-page number.</summary>
        public int OverflowPageCapacity => UsableSize - 4;

        public static bool IsValidPageSize(int pageSize) =>
            pageSize >= 512 && pageSize <= 65536 && (pageSize & (pageSize - 1)) == 0;

        public bool IsValid =>
            IsValidPageSize(PageSize) && ReservedBytes is >= 0 and <= 255 && UsableSize >= MinUsableSize;

        /// <summary>The largest reserved-byte count this page size allows.</summary>
        public static int MaxReservedFor(int pageSize) => Math.Min(255, pageSize - MinUsableSize);
    }

    /// <summary>How a cell's payload divides between the b-tree page and its overflow chain.</summary>
    public readonly record struct PayloadSplit(
        ulong Payload,
        ulong Local,
        ulong Spilled,
        ulong OverflowPages,
        ulong LastOverflowPageBytes)
    {
        public bool Overflows => Spilled > 0;

        public bool IsLargerThanSqliteAllows => Payload > SerialType.SqliteLengthLimit;
    }

    public static class SqlitePayload
    {
        /// <summary>
        /// The file format's rule: with P the payload size, X and M from the page, and
        /// K = M + ((P - M) % (U - 4)), a cell keeps all of P when P &lt;= X, otherwise K bytes
        /// when K &lt;= X, otherwise M bytes; the rest spills to overflow pages.
        /// </summary>
        public static PayloadSplit Split(ulong payload, PageGeometry page)
        {
            if (!page.IsValid)
                throw new ArgumentException("Not a valid SQLite page geometry.", nameof(page));

            ulong x = (ulong)page.MaxLocal;
            ulong m = (ulong)page.MinLocal;
            ulong perPage = (ulong)page.OverflowPageCapacity;

            if (payload <= x)
                return new PayloadSplit(payload, payload, 0, 0, 0);

            ulong k = m + (payload - m) % perPage;
            ulong local = k <= x ? k : m;
            ulong spilled = payload - local;
            ulong pages = spilled / perPage + (spilled % perPage == 0 ? 0UL : 1UL);
            ulong last = spilled - (pages - 1) * perPage;
            return new PayloadSplit(payload, local, spilled, pages, last);
        }
    }
}
