using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using VarIntCalculator.Core;

namespace VarIntCalculator.Tests
{
    /// <summary>
    /// The calculator's test suite. The numeric tests check the app's own Core code against
    /// vectors from reference implementations (real SQLite files, Google's protobuf runtime);
    /// the display tests drive CalculatorModel the way the keypad does.
    /// </summary>
    internal static class Program
    {
        private static int _tests, _failedTests, _checks;
        private static List<string> _currentFailures = new();

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Varint Calculator tests");
            Console.WriteLine();

            Run("SQLite varints: decode the bytes SQLite wrote", SqliteVarintsDecode);
            Run("SQLite varints: encode as SQLite does", SqliteVarintsEncode);
            Run("SQLite varints: the 9th byte keeps all 8 bits", SqliteNinthByte);
            Run("SQLite varints: incomplete input and non-minimal forms", SqliteEdgeCases);
            Run("SQLite serial types: sizes match real records", SqliteSerialTypes);
            Run("SQLite local payload: matches cells SQLite split", SqliteLocalPayload);
            Run("SQLite page geometry: limits", PageGeometryLimits);
            Run("Protobuf varints: encode as Google's runtime does", ProtobufEncode);
            Run("Protobuf varints: decode as Google's runtime does", ProtobufDecode);
            Run("Protobuf tags: field number and wire type", ProtobufTags);
            Run("Input: pasted hex and numbers", PastedInput);
            Run("Display: SQLite decode", DisplaySqliteDecode);
            Run("Display: overflow and page settings", DisplayOverflow);
            Run("Display: protobuf decode", DisplayProtobufDecode);
            Run("Display: pending and error states", DisplayPendingAndErrors);
            Run("Display: encode", DisplayEncode);
            Run("Display: switching format and direction", DisplaySwitching);
            Run("Display: keypad limits and history snapshots", DisplayKeypadAndHistory);
            Run("Text: no em or en dashes in user-facing text", NoDashes);
            Run("Text: US spelling in user-facing text", UsSpelling);
            Run("Themes: text contrast meets WCAG AA in all three themes", ThemeContrast);

            Console.WriteLine();
            Console.WriteLine($"{_tests - _failedTests} of {_tests} tests passed ({_checks:N0} checks)");
            return _failedTests == 0 ? 0 : 1;
        }

        // ================================================================ harness

        private static void Run(string name, Action test)
        {
            _tests++;
            _currentFailures = new List<string>();
            try
            {
                test();
            }
            catch (Exception ex)
            {
                _currentFailures.Add("threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (_currentFailures.Count == 0)
            {
                Console.WriteLine("  PASS  " + name);
                return;
            }

            _failedTests++;
            Console.WriteLine("  FAIL  " + name);
            foreach (string f in _currentFailures.Take(12))
                Console.WriteLine("          " + f);
            if (_currentFailures.Count > 12)
                Console.WriteLine($"          ... and {_currentFailures.Count - 12} more");
        }

        private static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition)
                _currentFailures.Add(message);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            _checks++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                _currentFailures.Add($"{what}: expected [{expected}], got [{actual}]");
        }

        private static JsonElement[] Vectors(string file)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Vectors", file);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("vectors").EnumerateArray().Select(e => e.Clone()).ToArray();
        }

        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        private static string Spaced(string hex) => HexText.Spaced(Hex(hex));

        private static ulong U(JsonElement e, string name) => ulong.Parse(e.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

        private static long S(JsonElement e, string name) => long.Parse(e.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

        // ================================================================ SQLite

        private static void SqliteVarintsDecode()
        {
            var vectors = Vectors("sqlite_varints.json");
            Check(vectors.Length > 1000, "expected over 1,000 SQLite varint vectors");
            foreach (var v in vectors)
            {
                string hex = v.GetProperty("hex").GetString()!;
                var d = SqliteVarint.Decode(Hex(hex));
                Equal(DecodeStatus.Ok, d.Status, $"{hex} status");
                Equal(U(v, "unsigned"), d.Value, $"{hex} value");
                Equal(hex.Length / 2, d.Length, $"{hex} length");
                Equal(S(v, "signed"), unchecked((long)d.Value), $"{hex} as signed");
                Check(d.IsMinimal, $"{hex}: SQLite always writes the shortest form, so it must read as minimal");

                // A byte after the varint is trailing, never part of it.
                var withTail = SqliteVarint.Decode(Hex(hex + "7F"));
                Equal(d.Value, withTail.Value, $"{hex}+7F value");
                Equal(1, withTail.TrailingBytes, $"{hex}+7F trailing bytes");
            }
        }

        private static void SqliteVarintsEncode()
        {
            foreach (var v in Vectors("sqlite_varints.json"))
            {
                string hex = v.GetProperty("hex").GetString()!;
                Equal(hex, HexText.Compact(SqliteVarint.Encode(U(v, "unsigned"))), $"encode {v.GetProperty("unsigned").GetString()}");
                Equal(hex, HexText.Compact(SqliteVarint.Encode(S(v, "signed"))), $"encode signed {v.GetProperty("signed").GetString()}");
                Equal(hex.Length / 2, SqliteVarint.EncodedLength(U(v, "unsigned")), $"length of {hex}");
            }
        }

        private static void SqliteNinthByte()
        {
            // Rowid -129: SQLite's own bytes (in the vectors); the 9th byte 7F is taken whole.
            var d = SqliteVarint.Decode(Hex("FFFFFFFFFFFFFFFF7F"));
            Equal(18446744073709551487UL, d.Value, "FF x8 7F");
            Equal(-129L, unchecked((long)d.Value), "FF x8 7F as signed");
            Equal(9, d.Length, "FF x8 7F length");

            d = SqliteVarint.Decode(Hex("FFFFFFFFFFFFFFFFFF"));
            Equal(ulong.MaxValue, d.Value, "FF x9");

            // 2^56 is the smallest value that needs nine bytes.
            Equal("80C080808080808000", HexText.Compact(SqliteVarint.Encode(1UL << 56)), "2^56");
            Equal("FFFFFFFFFFFFFF7F", HexText.Compact(SqliteVarint.Encode((1UL << 56) - 1)), "2^56 - 1");

            // A 10th byte is never read.
            d = SqliteVarint.Decode(Hex("FFFFFFFFFFFFFFFFFF01"));
            Equal(9, d.Length, "10 bytes: varint length");
            Equal(1, d.TrailingBytes, "10 bytes: trailing");
        }

        private static void SqliteEdgeCases()
        {
            Equal(DecodeStatus.Empty, SqliteVarint.Decode(Array.Empty<byte>()).Status, "empty");
            Equal(DecodeStatus.Incomplete, SqliteVarint.Decode(Hex("81")).Status, "81");
            Equal(DecodeStatus.Incomplete, SqliteVarint.Decode(Hex("FFFFFFFFFFFFFFFF")).Status, "FF x8");

            var d = SqliteVarint.Decode(Hex("8001"));
            Equal(1UL, d.Value, "80 01");
            Check(!d.IsMinimal, "80 01 is not minimal");
            Equal(1, d.MinimalLength, "80 01 minimal length");

            d = SqliteVarint.Decode(Hex("808080808080808001"));
            Equal(1UL, d.Value, "80 x8 01");
            Check(!d.IsMinimal, "nine-byte 1 is not minimal");
        }

        private static void SqliteSerialTypes()
        {
            var vectors = Vectors("sqlite_serial_types.json");
            Check(vectors.Length >= 20, "expected at least 20 serial types");
            var seen = new HashSet<ulong>();
            foreach (var v in vectors)
            {
                ulong code = U(v, "serialType");
                seen.Add(code);
                var st = SerialType.From(code);
                Equal((ulong?)v.GetProperty("contentBytes").GetInt64(), st.KnownContentBytes, $"serial type {code} content size");
            }
            foreach (ulong code in new ulong[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 13 })
                Check(seen.Contains(code), $"the vectors include serial type {code}");

            Equal(SerialKind.Reserved, SerialType.From(10).Kind, "10");
            Equal(SerialKind.Reserved, SerialType.From(11).Kind, "11");
            Equal(SerialKind.Blob, SerialType.From(160).Kind, "160");
            Equal(74UL, SerialType.From(160).ContentBytes, "160 size");
            Equal(SerialKind.Text, SerialType.From(161).Kind, "161");
            Equal(74UL, SerialType.From(161).ContentBytes, "161 size");
            Equal(161UL, SerialType.ForText(74), "text 74");
            Equal(160UL, SerialType.ForBlob(74), "blob 74");
            Check(!SerialType.From(12 + 2UL * int.MaxValue).IsLongerThanSqliteAllows, "a BLOB of 2^31-1 bytes is within the limit");
            Check(SerialType.From(12 + 2UL * int.MaxValue + 2).IsLongerThanSqliteAllows, "a BLOB of 2^31 bytes is past the limit");
        }

        private static void SqliteLocalPayload()
        {
            var vectors = Vectors("sqlite_payload.json");
            Check(vectors.Length > 1000, "expected over 1,000 payload vectors");
            int overflowing = 0, index = 0, reserved = 0;
            foreach (var v in vectors)
            {
                int pageSize = v.GetProperty("pageSize").GetInt32();
                int res = v.GetProperty("reserved").GetInt32();
                var kind = v.GetProperty("kind").GetString() == "table" ? BTreeKind.TableLeaf : BTreeKind.Index;
                ulong p = v.GetProperty("payload").GetUInt64();
                var page = new PageGeometry(pageSize, res, kind);
                var split = SqlitePayload.Split(p, page);
                string what = $"{pageSize}/{res}/{kind} P={p}";
                Equal(v.GetProperty("local").GetUInt64(), split.Local, what + " local");
                Equal(v.GetProperty("overflowPages").GetUInt64(), split.OverflowPages, what + " overflow pages");
                Equal(p, split.Local + split.Spilled, what + " local + spilled");
                Check(split.Local >= (ulong)Math.Min((ulong)page.MinLocal, p), what + ": never less than M");
                if (split.Overflows)
                {
                    overflowing++;
                    Check(split.LastOverflowPageBytes > 0 && split.LastOverflowPageBytes <= (ulong)page.OverflowPageCapacity, what + " last page bytes");
                }
                if (kind == BTreeKind.Index) index++;
                if (res > 0) reserved++;
            }
            Check(overflowing > 500, "the vectors cover overflowing cells");
            Check(index > 300, "the vectors cover index cells");
            Check(reserved > 200, "the vectors cover reserved bytes");

            // The worked example in the tutorial.
            var s = SqlitePayload.Split(5000, PageGeometry.Default);
            Equal(908UL, s.Local, "P=5000 local");
            Equal(4092UL, s.Spilled, "P=5000 spilled");
            Equal(1UL, s.OverflowPages, "P=5000 pages");

            // The arithmetic holds at the top of the range.
            s = SqlitePayload.Split(ulong.MaxValue, PageGeometry.Default);
            Equal(ulong.MaxValue, s.Local + s.Spilled, "2^64-1 adds up");
        }

        private static void PageGeometryLimits()
        {
            var g = PageGeometry.Default;
            Equal(4061, g.MaxLocal, "4096 table X");
            Equal(489, g.MinLocal, "4096 M");
            Equal(1002, (g with { Kind = BTreeKind.Index }).MaxLocal, "4096 index X");
            Equal(4092, g.OverflowPageCapacity, "4096 overflow page capacity");
            Check(new PageGeometry(512, 32, BTreeKind.TableLeaf).IsValid, "512 with 32 reserved is valid");
            Check(!new PageGeometry(512, 33, BTreeKind.TableLeaf).IsValid, "512 with 33 reserved leaves under 480 usable");
            Check(!new PageGeometry(1000, 0, BTreeKind.TableLeaf).IsValid, "1000 is not a power of two");
            Check(!new PageGeometry(131072, 0, BTreeKind.TableLeaf).IsValid, "131072 is too large");
            Check(new PageGeometry(65536, 255, BTreeKind.Index).IsValid, "65536 with 255 reserved is valid");
            Equal(32, PageGeometry.MaxReservedFor(512), "max reserved for 512");
            Equal(255, PageGeometry.MaxReservedFor(4096), "max reserved for 4096");
        }

        // ================================================================ Protobuf

        private static void ProtobufEncode()
        {
            var vectors = Vectors("protobuf_encode.json");
            Check(vectors.Length > 400, "expected over 400 protobuf encode vectors");
            foreach (var v in vectors)
            {
                string kind = v.GetProperty("kind").GetString()!;
                string hex = v.GetProperty("hex").GetString()!;
                string value = v.GetProperty("value").GetString()!;
                byte[] actual = kind switch
                {
                    "uint64" => ProtobufVarint.Encode(ulong.Parse(value, CultureInfo.InvariantCulture)),
                    "int64" => ProtobufVarint.Encode(long.Parse(value, CultureInfo.InvariantCulture)),
                    _ => ProtobufVarint.Encode(ProtobufVarint.ZigZagEncode(long.Parse(value, CultureInfo.InvariantCulture))),
                };
                Equal(hex, HexText.Compact(actual), $"{kind} {value}");
                Equal(hex.Length / 2, ProtobufVarint.EncodedLength(ProtobufVarint.Decode(Hex(hex)).Value), $"{kind} {value} length");
            }
        }

        private static void ProtobufDecode()
        {
            var vectors = Vectors("protobuf_decode.json");
            Check(vectors.Length > 500, "expected over 500 protobuf decode vectors");
            int invalid = 0, dropped = 0;
            foreach (var v in vectors)
            {
                string hex = v.GetProperty("hex").GetString()!;
                var d = ProtobufVarint.Decode(Hex(hex));
                bool valid = v.GetProperty("valid").GetBoolean();
                if (!valid)
                {
                    invalid++;
                    Check(!d.IsOk, $"{hex}: the runtime refuses it, so must the calculator");
                    continue;
                }
                Check(d.IsOk, $"{hex}: the runtime reads it");
                Equal(U(v, "uint64"), d.Value, $"{hex} uint64");
                Equal(S(v, "int64"), unchecked((long)d.Value), $"{hex} int64");
                Equal(S(v, "sint64"), ProtobufVarint.ZigZagDecode(d.Value), $"{hex} sint64");
                Equal(int.Parse(v.GetProperty("int32").GetString()!, CultureInfo.InvariantCulture), unchecked((int)(uint)d.Value), $"{hex} int32");
                Equal(hex.Length / 2, d.Length, $"{hex} length");
                if (d.DroppedHighBits) dropped++;
            }
            Check(invalid >= 3, "the vectors include varints the runtime refuses");
            Check(dropped >= 2, "the vectors include a 10th byte with bits past 64");

            Equal(DecodeStatus.Incomplete, ProtobufVarint.Decode(Hex("81")).Status, "81");
            Equal(DecodeStatus.NoTerminator, ProtobufVarint.Decode(Hex("FFFFFFFFFFFFFFFFFFFF")).Status, "FF x10");
            Equal(300UL, ProtobufVarint.Decode(Hex("AC02")).Value, "AC 02");
            Equal(150L, ProtobufVarint.ZigZagDecode(300), "zigzag 300");
            Equal(-1, ProtobufVarint.ZigZagDecode32(1), "zigzag32 1");
        }

        private static void ProtobufTags()
        {
            foreach (var v in Vectors("protobuf_tags.json"))
            {
                ulong tag = U(v, "tag");
                var t = TagReading.From(tag);
                Check(t.IsValid, $"tag {tag} is valid");
                Equal(v.GetProperty("field").GetUInt64(), t.FieldNumber, $"tag {tag} field");
                Equal(v.GetProperty("wireType").GetInt32(), t.WireType, $"tag {tag} wire type");
                Equal(tag, t.ToTag(), $"tag {tag} round trip");
            }
            Check(!TagReading.From(0).IsValid, "0 is not a tag");
            Check(!TagReading.From(7).IsValid, "field 0 is not a tag");
            Check(!TagReading.From(14).IsValid, "wire type 6 is not a tag");
            Check(!TagReading.From(15).IsValid, "wire type 7 is not a tag");
            Check(!TagReading.From(1UL << 32).IsValid, "33 bits is not a tag");
            Check(TagReading.From(uint.MaxValue - 2).IsValid, "field 2^29-1, wire type 5 is a tag");
            Equal("LEN", TagReading.From(0x0A).WireTypeName, "0A wire type");
            Check(TagReading.From(19000UL << 3).IsReservedFieldNumber, "19000 is reserved");
        }

        private static void PastedInput()
        {
            Equal("8120", HexText.NormalizePasted("81 20"), "81 20");
            Equal("8120", HexText.NormalizePasted("0x81 0x20"), "0x81 0x20");
            Equal("8120", HexText.NormalizePasted("0x81,0x20"), "0x81,0x20");
            Equal("8120", HexText.NormalizePasted(@"\x81\x20"), @"\x81\x20");
            Equal("8120", HexText.NormalizePasted("81:20"), "81:20");
            Equal("8120", HexText.NormalizePasted("81-20\r\n"), "81-20 newline");
            Equal("ABCDEF", HexText.NormalizePasted("ab cd ef"), "lower case");
            Equal(null, HexText.NormalizePasted("hello"), "hello");
            Equal(null, HexText.NormalizePasted("81 2G"), "81 2G");
            Equal(null, HexText.NormalizePasted("   "), "blank");

            Equal("1234567", DecimalText.NormalizePasted("1,234,567"), "1,234,567");
            Equal("-42", DecimalText.NormalizePasted(" -42 "), "-42");
            Equal("5", DecimalText.NormalizePasted("0005"), "0005");
            Equal("0", DecimalText.NormalizePasted("-0"), "-0");
            Equal(null, DecimalText.NormalizePasted("12a"), "12a");
            Equal(null, DecimalText.NormalizePasted("4-2"), "4-2");
            Equal(null, DecimalText.NormalizePasted("-"), "-");
            Equal("-1,234,567", DecimalText.Grouped("-1234567"), "grouped");
            Equal("18,446,744,073,709,551,615", DecimalText.Grouped("18446744073709551615"), "grouped max");

            var m = new CalculatorModel();
            Equal(PasteOutcome.Pasted, m.Paste("0x81 0x20"), "paste hex");
            Equal("8120", m.Input, "pasted input");
            Equal(PasteOutcome.Refused, m.Paste("not hex"), "paste text");
            Equal("8120", m.Input, "a refused paste keeps the input");
            Equal(PasteOutcome.Truncated, m.Paste("01 02 03 04 05 06 07 08 09 0A 0B 0C"), "paste 12 bytes");
            Equal("0102030405060708090A", m.Input, "the first 10 bytes are kept");
            m.Direction = CalcDirection.Encode;
            Equal(PasteOutcome.Pasted, m.Paste("-1,000"), "paste a number");
            Equal("-1000", m.Input, "pasted number");
            Equal(PasteOutcome.Refused, m.Paste("123456789012345678901"), "21 digits");
        }

        // ================================================================ display

        private static CalculatorModel Typed(string keys, VarintFormat format = VarintFormat.Sqlite, CalcDirection direction = CalcDirection.Decode)
        {
            var m = new CalculatorModel { Format = format, Direction = direction };
            foreach (char c in keys)
            {
                if (c == '-')
                    m.ToggleSign();
                else
                    m.Append(c);
            }
            return m;
        }

        private static ReadingRow? Row(CalculatorModel m, string label) =>
            m.Readings.FirstOrDefault(r => r.Label == label);

        private static void DisplaySqliteDecode()
        {
            var m = Typed("8120");
            Equal(DisplayState.Valid, m.State, "81 20 state");
            Equal("81 20", m.InputMain, "input");
            Equal("INTEGER", m.PrimaryLabel, "label");
            Equal("160", m.PrimaryValue, "value");
            Equal("160", m.PrimaryCopyText, "copy text");
            Equal("2-byte varint, big-endian", m.PrimaryNote, "note");
            Equal("HEX INPUT", m.InputLabel, "input label");
            Equal("SQLITE \u00B7 BIG-ENDIAN", m.FormatLabel, "format label");
            Equal("2 bytes", m.InputCount, "count");
            Equal("BLOB, 74 bytes", Row(m, "SERIAL TYPE")?.Value, "serial type");
            Equal("(160 - 12) / 2", Row(m, "SERIAL TYPE")?.Detail, "serial type detail");
            Equal("74", Row(m, "SERIAL TYPE")?.CopyText, "serial type copies the length");
            Check(Row(m, "AS A CELL'S PAYLOAD SIZE (P)")?.IsSection == true, "the payload readings have a section heading");
            Check(Row(m, "AS A CELL'S PAYLOAD SIZE (P)")?.Warning is null, "no warning for a small payload");
            Equal("No", Row(m, "Overflow")?.Value, "overflow");
            Equal("160 bytes", Row(m, "Local payload")?.Value, "local payload");
            Equal("4,096 bytes, table leaf", Row(m, "Page")?.Value, "page");
            Check(Row(m, "Page")?.OpensPageSettings == true, "the page row opens the settings");
            Check(Row(m, "SIGNED 64-BIT") is null, "no signed reading under 2^63");
            Equal("BLOB, 74 bytes", m.Summary, "summary");

            m = Typed("8121");
            Equal("TEXT, 74 bytes", Row(m, "SERIAL TYPE")?.Value, "161");
            m = Typed("00");
            Equal("NULL", Row(m, "SERIAL TYPE")?.Value, "0");
            m = Typed("06");
            Equal("Integer, 8 bytes", Row(m, "SERIAL TYPE")?.Value, "6");
            Equal("64-bit two's complement, big-endian", Row(m, "SERIAL TYPE")?.Detail, "6 detail");
            m = Typed("05");
            Equal("Integer, 6 bytes", Row(m, "SERIAL TYPE")?.Value, "5");
            m = Typed("07");
            Equal("Float, 8 bytes", Row(m, "SERIAL TYPE")?.Value, "7");
            m = Typed("08");
            Equal("Integer 0", Row(m, "SERIAL TYPE")?.Value, "8");
            m = Typed("0A");
            Equal("Reserved (10)", Row(m, "SERIAL TYPE")?.Value, "10");
            m = Typed("0C");
            Equal("BLOB, 0 bytes", Row(m, "SERIAL TYPE")?.Value, "12");
            m = Typed("0F");
            Equal("TEXT, 1 byte", Row(m, "SERIAL TYPE")?.Value, "15");

            // Rowid -129, as SQLite wrote it.
            m = Typed("FFFFFFFFFFFFFFFF7F");
            Equal("18,446,744,073,709,551,487", m.PrimaryValue, "FF x8 7F");
            Equal("-129", Row(m, "SIGNED 64-BIT")?.Value, "FF x8 7F signed");
            Equal("Signed -129", m.Summary, "signed summary");
            Check(Row(m, "SERIAL TYPE")?.Warning is not null, "a huge BLOB is flagged");
            Check(Row(m, "AS A CELL'S PAYLOAD SIZE (P)")?.Warning is not null, "a huge payload is flagged");

            // Trailing bytes and non-minimal forms.
            m = Typed("0501");
            Equal("5", m.PrimaryValue, "05 01");
            Equal("05", m.InputMain, "varint part");
            Equal(" 01", m.InputTrailing, "trailing part");
            Check(m.Notes.Any(n => n.Contains("not part of the varint")), "trailing note");
            m = Typed("8001");
            Equal("1", m.PrimaryValue, "80 01");
            Check(m.Notes.Any(n => n == "Not the shortest form: SQLite writes 1 as 01"), "non-minimal note");
            m = Typed("050");
            Equal(DisplayState.Valid, m.State, "05 then a half byte still shows 5");
            Equal(" 0", m.InputTrailing, "the half byte is shown after the varint");
        }

        private static void DisplayOverflow()
        {
            // A7 08 = 5,000: on a 4,096-byte page, 908 bytes stay local and 4,092 spill.
            var m = Typed("A708");
            Equal("5,000", m.PrimaryValue, "A7 08");
            Equal("Yes, 4,092 bytes on 1 page", Row(m, "Overflow")?.Value, "overflow");
            Check(Row(m, "Overflow")?.Emphasis == true, "overflow is emphasized");
            Equal("908 bytes", Row(m, "Local payload")?.Value, "local");
            Equal("908", Row(m, "Local payload")?.CopyText, "local copy");
            Equal("Then a 4-byte overflow page number", Row(m, "Local payload")?.Detail, "local detail");

            // X is the limit: 4,061 stays local, 4,062 spills.
            m = Typed(HexText.Compact(SqliteVarint.Encode(4061)));
            Equal("No", Row(m, "Overflow")?.Value, "P = X");
            m = Typed(HexText.Compact(SqliteVarint.Encode(4062)));
            Check(Row(m, "Overflow")?.Value.StartsWith("Yes") == true, "P = X + 1");

            m = Typed("A708");
            m.Page = new PageGeometry(1024, 0, BTreeKind.TableLeaf);
            Equal("1,024 bytes, table leaf", Row(m, "Page")?.Value, "page 1024");
            var expected = SqlitePayload.Split(5000, m.Page);
            Equal(NumberText.Bytes(expected.Local), Row(m, "Local payload")?.Value, "local on 1024");
            Equal($"Yes, {NumberText.Bytes(expected.Spilled)} on {expected.OverflowPages} pages", Row(m, "Overflow")?.Value, "overflow on 1024");

            m.Page = new PageGeometry(4096, 32, BTreeKind.Index);
            Equal("4,096 bytes (32 reserved), index", Row(m, "Page")?.Value, "page with reserved bytes");
            Equal(NumberText.Bytes(SqlitePayload.Split(5000, m.Page).Local), Row(m, "Local payload")?.Value, "local on index page");

            m.Page = new PageGeometry(512, 40, BTreeKind.Index);
            Equal(new PageGeometry(4096, 32, BTreeKind.Index), m.Page, "an invalid page is refused");
        }

        private static void DisplayProtobufDecode()
        {
            var m = Typed("AC02", VarintFormat.Protobuf);
            Equal("300", m.PrimaryValue, "AC 02");
            Equal("INTEGER (UNSIGNED)", m.PrimaryLabel, "label");
            Equal("2-byte varint, little-endian", m.PrimaryNote, "note");
            Equal("PROTOBUF \u00B7 LITTLE-ENDIAN", m.FormatLabel, "format label");
            Equal("150", Row(m, "SINT32 / SINT64")?.Value, "zigzag");
            Equal("Field 37, EGROUP (wire type 4)", Row(m, "FIELD TAG")?.Value, "tag");
            Check(Row(m, "INT64") is null && Row(m, "INT32 / INT64") is null, "no signed reading for a small value");

            m = Typed("08", VarintFormat.Protobuf);
            Equal("Field 1, VARINT (wire type 0)", Row(m, "FIELD TAG")?.Value, "08");
            Equal("A varint value follows", Row(m, "FIELD TAG")?.Detail, "08 detail");
            Equal("Field 1, VARINT (wire type 0)", m.Summary, "08 summary");
            m = Typed("12", VarintFormat.Protobuf);
            Equal("Field 2, LEN (wire type 2)", Row(m, "FIELD TAG")?.Value, "12");
            m = Typed("01", VarintFormat.Protobuf);
            Equal("true", Row(m, "BOOL")?.Value, "bool 1");
            Equal("-1", Row(m, "SINT32 / SINT64")?.Value, "zigzag 1");
            Equal("Not a valid tag", Row(m, "FIELD TAG")?.Value, "01 is not a tag");
            Equal("Field number 0 is not allowed", Row(m, "FIELD TAG")?.Detail, "01 reason");

            m = Typed("FFFFFFFFFFFFFFFFFF01", VarintFormat.Protobuf);
            Equal("18,446,744,073,709,551,615", m.PrimaryValue, "-1 as int64");
            Equal("-1", Row(m, "INT32 / INT64")?.Value, "int32/int64");
            Equal("SINT64", Row(m, "SINT64")?.Label, "sint64 only past 32 bits");
            Equal("A tag is at most 32 bits", Row(m, "FIELD TAG")?.Detail, "too big for a tag");

            m = Typed("FFFFFFFF0F", VarintFormat.Protobuf);
            Equal("-1", Row(m, "INT32")?.Value, "5-byte FF FF FF FF 0F as int32");

            m = Typed("FFFFFFFFFFFFFFFFFF7F", VarintFormat.Protobuf);
            Equal("18,446,744,073,709,551,615", m.PrimaryValue, "10th byte 7F");
            Check(m.Notes.Any(n => n.StartsWith("Byte 10 carries bits past 64")), "dropped-bits note");

            m = Typed("8100", VarintFormat.Protobuf);
            Check(m.Notes.Any(n => n == "Not the shortest form: a protobuf encoder writes 1 as 01"), "non-minimal protobuf");

            // The same bytes read little-endian.
            m = Typed("8120", VarintFormat.Protobuf);
            Equal("4,097", m.PrimaryValue, "81 20 little-endian");
        }

        private static void DisplayPendingAndErrors()
        {
            var m = new CalculatorModel();
            Equal(DisplayState.Empty, m.State, "empty");
            Equal("Type or paste hex bytes", m.Placeholder, "placeholder");
            Check(!m.HasPrimary && !m.HasReadings && !m.HasMessage, "nothing shown when empty");

            m = Typed("8");
            Equal(DisplayState.Pending, m.State, "8");
            Equal("Type the second digit of the byte", m.Message, "8 message");
            m = Typed("81");
            Equal(DisplayState.Pending, m.State, "81");
            Equal("81 has its high bit set, so the varint continues in the next byte", m.Message, "81 message");
            m = Typed("812");
            Equal("Type the second digit of byte 2", m.Message, "81 2 message");
            Equal("81 2", m.InputMain, "81 2 input");

            m = Typed("FFFFFFFFFFFFFFFFFFFF", VarintFormat.Protobuf);
            Equal(DisplayState.Error, m.State, "FF x10 protobuf");
            m = Typed("FFFFFFFFFFFFFFFFFFFF", VarintFormat.Sqlite);
            Equal(DisplayState.Valid, m.State, "FF x10 SQLite: nine bytes and a trailing one");
            Equal(1, m.Notes.Count(n => n.Contains("not part of the varint")), "one trailing byte");

            m = Typed("-", direction: CalcDirection.Encode);
            Equal(DisplayState.Pending, m.State, "a lone minus sign");
            m = Typed("18446744073709551616", direction: CalcDirection.Encode);
            Equal(DisplayState.Error, m.State, "2^64");
            m = Typed("-9223372036854775809", direction: CalcDirection.Encode);
            Equal(DisplayState.Error, m.State, "-2^63 - 1");
        }

        private static void DisplayEncode()
        {
            var m = Typed("160", direction: CalcDirection.Encode);
            Equal(DisplayState.Valid, m.State, "160");
            Equal("DECIMAL INPUT", m.InputLabel, "input label");
            Equal("SQLITE VARINT", m.PrimaryLabel, "label");
            Equal("81 20", m.PrimaryValue, "160 as SQLite");
            Equal("2 bytes, big-endian", m.PrimaryNote, "note");
            Equal("BLOB, 74 bytes", Row(m, "SERIAL TYPE")?.Value, "serial type");

            m = Typed("1234567", direction: CalcDirection.Encode);
            Equal("1,234,567", m.InputMain, "grouped input");

            m = Typed("-1", direction: CalcDirection.Encode);
            Equal("-1", m.Input, "typed -1");
            Equal("FF FF FF FF FF FF FF FF FF", m.PrimaryValue, "-1 as SQLite");
            Equal("18,446,744,073,709,551,615", Row(m, "UNSIGNED")?.Value, "-1 pattern");
            Check(Row(m, "SERIAL TYPE") is null, "no serial type for a negative number");
            Equal("9 bytes, big-endian", m.PrimaryNote, "-1 SQLite note");
            Check(m.Notes.Any(n => n.StartsWith("A negative value is stored as its 64-bit two's complement")), "-1 SQLite explains two's complement");

            m = Typed("300", VarintFormat.Protobuf, CalcDirection.Encode);
            Equal("AC 02", m.PrimaryValue, "300 as protobuf");
            Equal("D8 04", Row(m, "SINT32 / SINT64")?.Value, "300 zigzag bytes");
            Equal("Field 37, EGROUP (wire type 4)", Row(m, "FIELD TAG")?.Value, "300 as a tag");

            m = Typed("-1", VarintFormat.Protobuf, CalcDirection.Encode);
            Equal("FF FF FF FF FF FF FF FF FF 01", m.PrimaryValue, "-1 as int64");
            Equal("PROTOBUF VARINT (INT32, INT64)", m.PrimaryLabel, "-1 label");
            Equal("01", Row(m, "SINT32 / SINT64")?.Value, "-1 zigzag");
            Equal("10 bytes, little-endian", m.PrimaryNote, "-1 protobuf note");
            Check(m.Notes.Any(n => n.StartsWith("A negative int32 or int64 is sign-extended")), "-1 protobuf explains sign extension");

            m = Typed("-9223372036854775808", VarintFormat.Protobuf, CalcDirection.Encode);
            Equal("PROTOBUF VARINT (INT64)", m.PrimaryLabel, "int64 min label");
            Equal("FF FF FF FF FF FF FF FF FF 01", Row(m, "SINT64")?.Value, "int64 min zigzag");

            m = Typed("18446744073709551615", VarintFormat.Sqlite, CalcDirection.Encode);
            Equal("FF FF FF FF FF FF FF FF FF", m.PrimaryValue, "2^64 - 1 as SQLite");
            Equal("-1", Row(m, "SIGNED 64-BIT")?.Value, "2^64 - 1 signed");

            // Typing past leading zeros and signs.
            m = Typed("-05", direction: CalcDirection.Encode);
            Equal("-5", m.Input, "- 0 5");
            m = Typed("0007", direction: CalcDirection.Encode);
            Equal("7", m.Input, "0 0 0 7");
            m = Typed("5-", direction: CalcDirection.Encode);
            Equal("-5", m.Input, "the sign key after the digits");
            m = Typed("5--", direction: CalcDirection.Encode);
            Equal("5", m.Input, "the sign key twice");
        }

        private static void DisplaySwitching()
        {
            var m = Typed("8120");
            m.Format = VarintFormat.Protobuf;
            Equal("8120", m.Input, "switching format keeps the bytes");
            Equal("4,097", m.PrimaryValue, "and reads them little-endian");
            m.Format = VarintFormat.Sqlite;
            Equal("160", m.PrimaryValue, "and big-endian again");

            m.Direction = CalcDirection.Encode;
            Equal("160", m.Input, "decode to encode carries the integer");
            Equal("81 20", m.PrimaryValue, "and encodes it");
            m.Format = VarintFormat.Protobuf;
            Equal("A0 01", m.PrimaryValue, "160 little-endian");
            m.Direction = CalcDirection.Decode;
            Equal("A001", m.Input, "encode to decode carries the bytes");
            Equal("160", m.PrimaryValue, "and decodes them");

            m = Typed("81");
            m.Direction = CalcDirection.Encode;
            Equal("", m.Input, "an unfinished input is not carried");
            Check(m.IsEncode && !m.IsDecode && !m.HexKeysEnabled && m.SignKeyEnabled, "encode enables the sign key, not A-F");

            m.IsSqlite = false;
            Equal(VarintFormat.Sqlite, m.Format, "a radio button's false is ignored");
            m.IsProtobuf = true;
            Equal(VarintFormat.Protobuf, m.Format, "a radio button's true selects");
        }

        private static void DisplayKeypadAndHistory()
        {
            var m = new CalculatorModel();
            for (int i = 0; i < 20; i++)
                Check(m.Append('F'), $"digit {i + 1} fits");
            Check(!m.Append('F'), "a 21st hex digit does not");
            Check(!m.Append('G'), "G is not hex");
            Check(!m.ToggleSign(), "no sign in decode");
            Check(m.Backspace(), "backspace");
            Equal(19, m.Input.Length, "after backspace");
            Check(m.Clear(), "clear");
            Check(!m.Clear(), "clear when empty changes nothing");
            Check(!m.Backspace(), "backspace when empty changes nothing");
            Check(m.Append('a'), "lower case");
            Equal("A", m.Input, "stored as upper case");

            m = Typed("12345678901234567890", direction: CalcDirection.Encode);
            Check(!m.Append('1'), "a 21st decimal digit does not fit");
            Check(!m.Append('A'), "A in encode mode");

            m = Typed("8120");
            var h = m.Snapshot();
            Check(h is not null, "a valid result snapshots");
            Equal("81 20", h!.InputDisplay, "history input");
            Equal("160", h.Result, "history result");
            Equal("BLOB, 74 bytes", h.Summary, "history summary");
            Check(Typed("81").Snapshot() is null, "a pending input does not");

            var restored = new CalculatorModel();
            h.Format = VarintFormat.Protobuf;
            restored.Load(h);
            Equal("4,097", restored.PrimaryValue, "a history entry restores format and input");
            Check(h.SameCalculation(restored.Snapshot()!), "same calculation");
        }

        // ================================================================ text rules

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VarIntCalculator.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
        }

        private static IEnumerable<string> UserFacingSources()
        {
            string root = RepoRoot();
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Where(p =>
                {
                    string rel = Path.GetRelativePath(root, p).Replace('\\', '/');
                    return !rel.StartsWith("bin/") && !rel.StartsWith("obj/") && !rel.StartsWith("VarIntCalculator.Tests/") && !rel.StartsWith("tools/");
                });
        }

        /// <summary>The text a user can see: XAML as a whole, string literals in C#.</summary>
        private static IEnumerable<(string File, string Text)> UserFacingText()
        {
            var literal = new Regex("\\$?@?\"(?:[^\"\\\\]|\\\\.)*\"");
            foreach (string path in UserFacingSources())
            {
                string text = File.ReadAllText(path);
                if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    yield return (path, Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline));
                    continue;
                }
                foreach (Match lit in literal.Matches(Regex.Replace(text, "//[^\\n]*", "")))
                    yield return (path, lit.Value);
            }
        }

        private static void NoDashes()
        {
            int files = 0;
            foreach (var (file, text) in UserFacingText())
            {
                files++;
                foreach (string dash in new[] { "\u2014", "\u2013", "&#8212;", "&#8211;", "&mdash;", "&ndash;", "&#x2014;", "&#x2013;" })
                    Check(!text.Contains(dash, StringComparison.Ordinal), $"{Path.GetFileName(file)} contains {(dash.Length == 1 ? "U+" + ((int)dash[0]).ToString("X4") : dash)}");
            }
            Check(files > 5, "the scan found the sources");
        }

        /// <summary>
        /// Reads the three theme dictionaries as text and checks every foreground/background pair the
        /// windows use: 4.5:1 for text, 3:1 for the display edge and the large key legends.
        /// </summary>
        private static void ThemeContrast()
        {
            string root = RepoRoot();
            var brush = new Regex("<SolidColorBrush x:Key=\"(\\w+)\" Color=\"#([0-9A-Fa-f]{6,8})\"");
            (string Fg, string Bg, double Min)[] pairs =
            {
                ("PrimaryTextBrush", "DisplayBackgroundBrush", 4.5), ("PrimaryTextBrush", "SheetBackgroundBrush", 4.5),
                ("PrimaryTextBrush", "CardBackgroundBrush", 4.5), ("PrimaryTextBrush", "InputBackgroundBrush", 4.5),
                ("SecondaryTextBrush", "DisplayBackgroundBrush", 4.5), ("SecondaryTextBrush", "SheetBackgroundBrush", 4.5),
                ("SecondaryTextBrush", "CardBackgroundBrush", 4.5),
                ("MutedTextBrush", "DisplayBackgroundBrush", 4.5), ("MutedTextBrush", "SheetBackgroundBrush", 4.5),
                ("MutedTextBrush", "CardBackgroundBrush", 4.5), ("MutedTextBrush", "StatusBarBackgroundBrush", 4.5),
                ("AccentTextBrush", "DisplayBackgroundBrush", 4.5), ("AccentTextBrush", "SheetBackgroundBrush", 4.5),
                ("AccentTextBrush", "CardBackgroundBrush", 4.5),
                ("KickerTextBrush", "DisplayBackgroundBrush", 4.5), ("KickerTextBrush", "CardBackgroundBrush", 4.5),
                ("ErrorTextBrush", "DisplayBackgroundBrush", 4.5), ("ErrorTextBrush", "SheetBackgroundBrush", 4.5),
                ("ErrorTextBrush", "CardBackgroundBrush", 4.5), ("ValidBrush", "CardBackgroundBrush", 4.5),
                ("KeyDigitForegroundBrush", "KeyDigitBrush", 4.5), ("KeyFunctionForegroundBrush", "KeyFunctionBrush", 4.5),
                ("SegmentForegroundBrush", "SegmentTrackBrush", 4.5), ("SegmentForegroundBrush", "SegmentHoverBrush", 4.5),
                ("ValidBrush", "DisplayBackgroundBrush", 3.0), ("ErrorBrush", "DisplayBackgroundBrush", 3.0),
            };
            foreach (string theme in new[] { "Default", "Dark", "Light" })
            {
                var colors = brush.Matches(File.ReadAllText(Path.Combine(root, "Themes", theme + ".xaml")))
                    .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
                foreach (var (fg, bg, min) in pairs)
                {
                    Check(colors.ContainsKey(fg) && colors.ContainsKey(bg), $"{theme}: {fg} and {bg} are defined");
                    if (!colors.ContainsKey(fg) || !colors.ContainsKey(bg))
                        continue;
                    double r = Contrast(colors[fg], colors[bg]);
                    Check(r >= min, $"{theme}: {fg} on {bg} is {r:F2}:1, under {min}:1");
                }
                Check(Contrast("FFFFFF", colors["ToolTipBackgroundBrush"]) >= 4.5, $"{theme}: tooltip text");
            }
            Check(Contrast("FFFFFF", "2B64F8") >= 4.5, "white on the accent");
            Check(Contrast("FFFFFF", "C62838") >= 4.5, "white on the danger red");
        }

        private static double Contrast(string a, string b)
        {
            static double Luminance(string hex)
            {
                hex = hex[^6..];
                double C(int i)
                {
                    double v = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
                    return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * C(0) + 0.7152 * C(2) + 0.0722 * C(4);
            }
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static void UsSpelling()
        {
            string[] uk = { "colour", "behaviour", "organisation", "organise", "recognise", "recognised", "centre", "analyse",
                "analysed", "favour", "licence", "catalogue", "grey", "initialise", "serialise", "optimise", "customise",
                "normalise", "summarise", "minimise", "maximise", "cancelled", "travelling", "labelled" };
            var pattern = new Regex("\\b(" + string.Join("|", uk) + ")\\b", RegexOptions.IgnoreCase);
            foreach (var (file, text) in UserFacingText())
            {
                foreach (Match hit in pattern.Matches(text))
                    Check(false, $"{Path.GetFileName(file)}: UK spelling \"{hit.Value}\"");
            }
        }
    }
}
