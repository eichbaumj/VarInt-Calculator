using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

namespace VarIntCalculator.Core
{
    public enum CalcDirection
    {
        /// <summary>Hex bytes in, integer out.</summary>
        Decode,

        /// <summary>Whole number in, varint bytes out.</summary>
        Encode,
    }

    public enum DisplayState
    {
        Empty,
        Pending,
        Valid,
        Error,
    }

    /// <summary>One line in the display's readings: what the value means if it is read a certain way.</summary>
    public sealed class ReadingRow
    {
        public string Label { get; init; } = "";
        public string Value { get; init; } = "";
        public string? Detail { get; init; }
        public string? Warning { get; init; }
        public string? CopyText { get; init; }
        public bool Emphasis { get; init; }
        public bool IsSubRow { get; init; }

        /// <summary>A heading for the sub-rows under it: the label spans the row, no value.</summary>
        public bool IsSection { get; init; }
        public bool IsMono { get; init; }
        public bool OpensPageSettings { get; init; }
    }

    /// <summary>A calculation kept in the history list.</summary>
    public sealed class HistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public VarintFormat Format { get; set; }
        public CalcDirection Direction { get; set; }
        public string Input { get; set; } = "";
        public string InputDisplay { get; set; } = "";
        public string Result { get; set; } = "";
        public string? Summary { get; set; }

        public bool SameCalculation(HistoryEntry other) =>
            other.Format == Format && other.Direction == Direction && other.Input == Input;
    }

    public enum PasteOutcome
    {
        Pasted,
        Truncated,
        Refused,
    }

    /// <summary>
    /// The calculator: what has been typed, how it is read, and every string the display shows.
    /// No WPF types, so the tests drive it exactly as the keypad does.
    /// </summary>
    public sealed class CalculatorModel : INotifyPropertyChanged
    {
        /// <summary>Ten bytes hold the longest varint of either format.</summary>
        public const int MaxInputBytes = 10;

        private const string Dot = " · ";

        private VarintFormat _format = VarintFormat.Sqlite;
        private CalcDirection _direction = CalcDirection.Decode;
        private PageGeometry _page = PageGeometry.Default;
        private string _input = "";

        // Replaced (not cleared) on every change, so a binding sees a new list and redraws.
        private List<string> _notes = new();
        private List<ReadingRow> _readings = new();

        public CalculatorModel()
        {
            Recompute();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // ------------------------------------------------------------------ state

        public VarintFormat Format
        {
            get => _format;
            set
            {
                if (_format == value)
                    return;
                _format = value;
                Recompute();
            }
        }

        public CalcDirection Direction
        {
            get => _direction;
            set => SetDirection(value);
        }

        public PageGeometry Page
        {
            get => _page;
            set
            {
                if (_page == value || !value.IsValid)
                    return;
                _page = value;
                Recompute();
            }
        }

        public string Input => _input;

        public bool IsSqlite
        {
            get => _format == VarintFormat.Sqlite;
            set { if (value) Format = VarintFormat.Sqlite; }
        }

        public bool IsProtobuf
        {
            get => _format == VarintFormat.Protobuf;
            set { if (value) Format = VarintFormat.Protobuf; }
        }

        public bool IsDecode
        {
            get => _direction == CalcDirection.Decode;
            set { if (value) SetDirection(CalcDirection.Decode); }
        }

        public bool IsEncode
        {
            get => _direction == CalcDirection.Encode;
            set { if (value) SetDirection(CalcDirection.Encode); }
        }

        public bool HexKeysEnabled => _direction == CalcDirection.Decode;

        public bool SignKeyEnabled => _direction == CalcDirection.Encode;

        // ------------------------------------------------------------------ display

        public DisplayState State { get; private set; }
        public string InputLabel { get; private set; } = "";
        public string FormatLabel { get; private set; } = "";
        public string InputMain { get; private set; } = "";
        public string InputTrailing { get; private set; } = "";
        public string InputCount { get; private set; } = "";
        public string Placeholder { get; private set; } = "";
        public bool HasInput => _input.Length > 0;
        public bool ShowPlaceholder => _input.Length == 0;
        public bool IsError => State == DisplayState.Error;
        public string InputCopyText => _direction == CalcDirection.Decode ? HexText.SpacedDigits(_input) : _input;

        public string PrimaryLabel { get; private set; } = "";
        public string PrimaryValue { get; private set; } = "";
        public string PrimaryNote { get; private set; } = "";
        public string PrimaryCopyText { get; private set; } = "";
        public bool PrimaryIsBytes { get; private set; }
        public bool HasPrimary => State == DisplayState.Valid;

        public IReadOnlyList<string> Notes => _notes;
        public bool HasNotes => _notes.Count > 0;

        public string Message { get; private set; } = "";
        public bool HasMessage => Message.Length > 0;

        public IReadOnlyList<ReadingRow> Readings => _readings;
        public bool HasReadings => _readings.Count > 0;

        /// <summary>The status line's text when there is nothing else to say.</summary>
        public string IdleHint => _direction == CalcDirection.Decode
            ? "Type hex digits 0-9 and A-F, or paste with Ctrl+V"
            : "Type a whole number (- for negative), or paste with Ctrl+V";

        /// <summary>A short reading of the value for the history list.</summary>
        public string? Summary { get; private set; }

        // ------------------------------------------------------------------ input

        /// <summary>Adds one typed character. False when it does not apply or the input is full.</summary>
        public bool Append(char c)
        {
            if (_direction == CalcDirection.Decode)
            {
                if (!HexText.IsHexDigit(c) || _input.Length >= MaxInputBytes * 2)
                    return false;
                return SetInput(_input + char.ToUpperInvariant(c));
            }

            if (c is < '0' or > '9')
                return false;
            if (_input == "0")
                return SetInput(c.ToString());
            if (_input == "-0")
                return SetInput("-" + c);
            if (DigitCount(_input) >= DecimalText.MaxDigits)
                return false;
            return SetInput(_input + c);
        }

        public bool Backspace() => _input.Length > 0 && SetInput(_input[..^1]);

        public bool Clear() => SetInput("");

        /// <summary>Encode mode: switches the sign of the typed number.</summary>
        public bool ToggleSign()
        {
            if (_direction != CalcDirection.Encode)
                return false;
            return SetInput(_input.StartsWith('-') ? _input[1..] : "-" + _input);
        }

        /// <summary>Replaces the input with pasted text, read as hex (Decode) or a whole number (Encode).</summary>
        public PasteOutcome Paste(string? text)
        {
            if (_direction == CalcDirection.Decode)
            {
                string? digits = HexText.NormalizePasted(text);
                if (digits is null)
                    return PasteOutcome.Refused;
                bool truncated = digits.Length > MaxInputBytes * 2;
                SetInput(truncated ? digits[..(MaxInputBytes * 2)] : digits);
                return truncated ? PasteOutcome.Truncated : PasteOutcome.Pasted;
            }

            string? number = DecimalText.NormalizePasted(text);
            if (number is null || DigitCount(number) > DecimalText.MaxDigits)
                return PasteOutcome.Refused;
            SetInput(number);
            return PasteOutcome.Pasted;
        }

        /// <summary>Restores a calculation from the history list.</summary>
        public void Load(HistoryEntry entry) => Load(entry.Format, entry.Direction, entry.Input);

        /// <summary>
        /// Sets format, direction and input together. The input is checked like a paste, so a
        /// hand-edited history file cannot put anything but hex digits or a number on the display.
        /// </summary>
        public void Load(VarintFormat format, CalcDirection direction, string? input)
        {
            _format = Enum.IsDefined(format) ? format : VarintFormat.Sqlite;
            _direction = Enum.IsDefined(direction) ? direction : CalcDirection.Decode;
            if (_direction == CalcDirection.Decode)
            {
                string digits = HexText.NormalizePasted(input) ?? "";
                _input = digits.Length > MaxInputBytes * 2 ? digits[..(MaxInputBytes * 2)] : digits;
            }
            else
            {
                string number = DecimalText.NormalizePasted(input) ?? "";
                _input = DigitCount(number) > DecimalText.MaxDigits ? "" : number;
            }
            Recompute();
        }

        /// <summary>
        /// Switches between Decode and Encode, carrying a complete result across: the decoded
        /// integer becomes the number to encode, the encoded bytes become the hex to decode.
        /// </summary>
        public void SetDirection(CalcDirection direction)
        {
            if (_direction == direction)
                return;

            string carried = "";
            if (State == DisplayState.Valid)
            {
                carried = direction == CalcDirection.Encode
                    ? PrimaryCopyText
                    : PrimaryCopyText.Replace(" ", "", StringComparison.Ordinal);
            }
            _direction = direction;
            _input = carried;
            Recompute();
        }

        public HistoryEntry? Snapshot()
        {
            if (State != DisplayState.Valid)
                return null;
            return new HistoryEntry
            {
                Format = _format,
                Direction = _direction,
                Input = _input,
                InputDisplay = _direction == CalcDirection.Decode ? HexText.SpacedDigits(_input) : DecimalText.Grouped(_input),
                Result = PrimaryValue,
                Summary = Summary,
            };
        }

        private bool SetInput(string value)
        {
            if (value == _input)
                return false;
            _input = value;
            Recompute();
            return true;
        }

        private static int DigitCount(string input) => input.StartsWith('-') ? input.Length - 1 : input.Length;

        // ------------------------------------------------------------------ reading

        private void Recompute()
        {
            _notes = new List<string>();
            _readings = new List<ReadingRow>();
            Message = "";
            PrimaryLabel = PrimaryValue = PrimaryNote = PrimaryCopyText = "";
            PrimaryIsBytes = false;
            InputTrailing = "";
            InputCount = "";
            Summary = null;

            FormatLabel = _format == VarintFormat.Sqlite ? "SQLITE" + Dot + "BIG-ENDIAN" : "PROTOBUF" + Dot + "LITTLE-ENDIAN";

            if (_direction == CalcDirection.Decode)
                RecomputeDecode();
            else
                RecomputeEncode();

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        private void RecomputeDecode()
        {
            InputLabel = "HEX INPUT";
            Placeholder = "Type or paste hex bytes";
            if (_input.Length == 0)
            {
                State = DisplayState.Empty;
                InputMain = "";
                return;
            }

            byte[] bytes = HexText.ToBytes(_input);
            InputCount = NumberText.Plural((ulong)bytes.Length, "byte", "bytes");
            VarintDecode d = _format == VarintFormat.Sqlite ? SqliteVarint.Decode(bytes) : ProtobufVarint.Decode(bytes);

            if (!d.IsOk)
            {
                InputMain = HexText.SpacedDigits(_input);
                switch (d.Status)
                {
                    case DecodeStatus.Empty:
                        State = DisplayState.Pending;
                        Message = "Type the second digit of the byte";
                        break;
                    case DecodeStatus.Incomplete when _input.Length % 2 == 1:
                        State = DisplayState.Pending;
                        Message = $"Type the second digit of byte {bytes.Length + 1}";
                        break;
                    case DecodeStatus.Incomplete:
                        State = DisplayState.Pending;
                        Message = $"{bytes[^1]:X2} has its high bit set, so the varint continues in the next byte";
                        break;
                    default:
                        State = DisplayState.Error;
                        Message = "None of these 10 bytes ends the varint: a protobuf varint is at most 10 bytes";
                        break;
                }
                return;
            }

            State = DisplayState.Valid;
            InputMain = HexText.Spaced(bytes.AsSpan(0, d.Length));
            string rest = _input[(d.Length * 2)..];
            InputTrailing = rest.Length > 0 ? " " + HexText.SpacedDigits(rest) : "";

            ulong v = d.Value;
            PrimaryValue = NumberText.Grouped(v);
            PrimaryCopyText = NumberText.Plain(v);

            if (d.TrailingBytes > 0)
            {
                _notes.Add(d.TrailingBytes == 1
                    ? "The byte after it is not part of the varint"
                    : $"The {d.TrailingBytes} bytes after it are not part of the varint");
            }

            if (_format == VarintFormat.Sqlite)
            {
                PrimaryLabel = "INTEGER";
                PrimaryNote = $"{d.Length}-byte varint, big-endian";
                if (!d.IsMinimal)
                    _notes.Add($"Not the shortest form: SQLite writes {PrimaryValue} as {HexText.Spaced(SqliteVarint.Encode(v))}");
                AddSqliteReadings(v);
            }
            else
            {
                PrimaryLabel = "INTEGER (UNSIGNED)";
                PrimaryNote = $"{d.Length}-byte varint, little-endian";
                if (d.DroppedHighBits)
                    _notes.Add("Byte 10 carries bits past 64: the value is the low 64 bits, as protobuf parsers read it");
                else if (!d.IsMinimal)
                    _notes.Add($"Not the shortest form: a protobuf encoder writes {PrimaryValue} as {HexText.Spaced(ProtobufVarint.Encode(v))}");
                AddProtobufReadings(v);
            }
        }

        private void RecomputeEncode()
        {
            InputLabel = "DECIMAL INPUT";
            Placeholder = "Type or paste a whole number";
            InputMain = DecimalText.Grouped(_input);
            if (_input.Length == 0)
            {
                State = DisplayState.Empty;
                return;
            }
            if (_input == "-")
            {
                State = DisplayState.Pending;
                Message = "Type the digits of the negative number";
                return;
            }
            if (!DecimalText.TryParse(_input, out BigInteger n) || !DecimalText.IsInRange(n))
            {
                State = DisplayState.Error;
                Message = "Out of range: a varint holds 0 to 18,446,744,073,709,551,615, or down to -9,223,372,036,854,775,808 as a signed value";
                return;
            }

            State = DisplayState.Valid;
            PrimaryIsBytes = true;
            bool negative = n.Sign < 0;
            ulong pattern = negative ? unchecked((ulong)(long)n) : (ulong)n;

            if (_format == VarintFormat.Sqlite)
            {
                byte[] bytes = SqliteVarint.Encode(pattern);
                PrimaryLabel = "SQLITE VARINT";
                SetPrimaryBytes(bytes);
                if (negative)
                {
                    PrimaryNote = "9 bytes, big-endian";
                    _notes.Add("A negative value is stored as its 64-bit two's complement, as SQLite stores a negative rowid");
                    AddUnsignedPatternRow(pattern);
                    Summary = "Signed " + NumberText.Grouped((long)n);
                }
                else
                {
                    PrimaryNote = $"{NumberText.Bytes((ulong)bytes.Length)}, big-endian";
                    AddSqliteReadings(pattern);
                }
            }
            else
            {
                byte[] bytes = ProtobufVarint.Encode(pattern);
                SetPrimaryBytes(bytes);
                if (negative)
                {
                    long s = (long)n;
                    PrimaryLabel = "PROTOBUF VARINT (INT32, INT64)";
                    if (s < int.MinValue)
                        PrimaryLabel = "PROTOBUF VARINT (INT64)";
                    PrimaryNote = "10 bytes, little-endian";
                    _notes.Add("A negative int32 or int64 is sign-extended to 64 bits, so it always takes 10 bytes");
                    AddZigZagEncodedRow(s);
                    AddUnsignedPatternRow(pattern);
                    Summary = "ZigZag " + HexText.Spaced(ProtobufVarint.Encode(ProtobufVarint.ZigZagEncode(s)));
                }
                else
                {
                    PrimaryLabel = "PROTOBUF VARINT";
                    PrimaryNote = $"{NumberText.Bytes((ulong)bytes.Length)}, little-endian";
                    if (pattern <= long.MaxValue)
                        AddZigZagEncodedRow((long)pattern);
                    AddTagRow(pattern);
                }
            }
        }

        private void SetPrimaryBytes(byte[] bytes)
        {
            PrimaryValue = HexText.Spaced(bytes);
            PrimaryCopyText = PrimaryValue;
        }

        // ------------------------------------------------------------------ SQLite readings

        private void AddSqliteReadings(ulong v)
        {
            string? signedSummary = null;
            if (v >= 1UL << 63)
            {
                long signed = unchecked((long)v);
                _readings.Add(new ReadingRow
                {
                    Label = "SIGNED 64-BIT",
                    Value = NumberText.Grouped(signed),
                    CopyText = NumberText.Plain(signed),
                    Detail = "Two's complement, as a negative rowid is stored",
                });
                signedSummary = "Signed " + NumberText.Grouped(signed);
            }

            SerialType st = SerialType.From(v);
            ReadingRow serial = SerialTypeRow(st);
            _readings.Add(serial);
            Summary = signedSummary ?? serial.Value;

            AddPayloadRows(v);
        }

        private static ReadingRow SerialTypeRow(SerialType st)
        {
            string code = NumberText.Grouped(st.Code);
            (string value, string? detail) = st.Kind switch
            {
                SerialKind.Null => ("NULL", "No content bytes"),
                SerialKind.Integer => ($"Integer, {NumberText.Bytes(st.ContentBytes)}",
                    $"{st.ContentBytes * 8}-bit two's complement, big-endian"),
                SerialKind.Float => ("Float, 8 bytes", "64-bit IEEE 754, big-endian"),
                SerialKind.Zero => ("Integer 0", "No content bytes (schema format 4 and later)"),
                SerialKind.One => ("Integer 1", "No content bytes (schema format 4 and later)"),
                SerialKind.Reserved => ($"Reserved ({code})", "Internal use: not found in a well-formed database file"),
                SerialKind.Blob => ($"BLOB, {NumberText.Bytes(st.ContentBytes)}", $"({code} - 12) / 2"),
                _ => ($"TEXT, {NumberText.Bytes(st.ContentBytes)}", $"({code} - 13) / 2"),
            };

            return new ReadingRow
            {
                Label = "SERIAL TYPE",
                Value = value,
                Detail = detail,
                Warning = st.IsLongerThanSqliteAllows
                    ? $"Longer than SQLite allows for a {(st.Kind == SerialKind.Blob ? "BLOB" : "TEXT")} (under 2^31 bytes)"
                    : null,
                CopyText = st.Kind is SerialKind.Blob or SerialKind.Text ? NumberText.Plain(st.ContentBytes) : value,
            };
        }

        private void AddPayloadRows(ulong payload)
        {
            PayloadSplit split = SqlitePayload.Split(payload, _page);

            _readings.Add(new ReadingRow
            {
                IsSection = true,
                Label = "AS A CELL'S PAYLOAD SIZE (P)",
                Warning = split.IsLargerThanSqliteAllows ? "Larger than SQLite allows for a row (under 2^31 bytes)" : null,
            });
            _readings.Add(new ReadingRow
            {
                IsSubRow = true,
                Label = "Page",
                Value = PageText(_page),
                OpensPageSettings = true,
            });
            _readings.Add(new ReadingRow
            {
                IsSubRow = true,
                Label = "Overflow",
                Value = split.Overflows
                    ? $"Yes, {NumberText.Bytes(split.Spilled)} on {NumberText.Plural(split.OverflowPages, "page", "pages")}"
                    : "No",
                Emphasis = split.Overflows,
                CopyText = split.Overflows ? NumberText.Plain(split.Spilled) : null,
            });
            _readings.Add(new ReadingRow
            {
                IsSubRow = true,
                Label = "Local payload",
                Value = NumberText.Bytes(split.Local),
                CopyText = NumberText.Plain(split.Local),
                Detail = split.Overflows ? "Then a 4-byte overflow page number" : null,
            });
        }

        public static string PageText(PageGeometry page)
        {
            string size = NumberText.Grouped(page.PageSize) + " bytes";
            if (page.ReservedBytes > 0)
                size += $" ({page.ReservedBytes} reserved)";
            return size + (page.Kind == BTreeKind.TableLeaf ? ", table leaf" : ", index");
        }

        // ------------------------------------------------------------------ protobuf readings

        private void AddProtobufReadings(ulong v)
        {
            if (v >= 1UL << 63)
            {
                long signed = unchecked((long)v);
                _readings.Add(new ReadingRow
                {
                    Label = signed >= int.MinValue ? "INT32 / INT64" : "INT64",
                    Value = NumberText.Grouped(signed),
                    CopyText = NumberText.Plain(signed),
                    Detail = "Two's complement: a negative value is sign-extended to 10 bytes",
                });
            }
            else if (v > int.MaxValue && v <= uint.MaxValue)
            {
                int low = unchecked((int)(uint)v);
                _readings.Add(new ReadingRow
                {
                    Label = "INT32",
                    Value = NumberText.Grouped(low),
                    CopyText = NumberText.Plain(low),
                    Detail = "An int32 field keeps the low 32 bits",
                });
            }

            long zigzag = ProtobufVarint.ZigZagDecode(v);
            _readings.Add(new ReadingRow
            {
                Label = v <= uint.MaxValue ? "SINT32 / SINT64" : "SINT64",
                Value = NumberText.Grouped(zigzag),
                CopyText = NumberText.Plain(zigzag),
                Detail = "ZigZag: even values are positive, odd values negative",
            });

            if (v <= 1)
                _readings.Add(new ReadingRow { Label = "BOOL", Value = v == 1 ? "true" : "false", CopyText = v == 1 ? "true" : "false" });

            ReadingRow tag = AddTagRow(v);
            Summary = tag.CopyText is null ? $"sint64 {NumberText.Grouped(zigzag)}" : tag.Value;
        }

        private ReadingRow AddTagRow(ulong v)
        {
            TagReading tag = TagReading.From(v);
            ReadingRow row = tag.IsValid
                ? new ReadingRow
                {
                    Label = "FIELD TAG",
                    Value = $"Field {NumberText.Grouped(tag.FieldNumber)}, {tag.WireTypeName} (wire type {tag.WireType})",
                    CopyText = NumberText.Plain(tag.FieldNumber),
                    Detail = WireTypeNames.Follows(tag.WireType),
                    Warning = tag.IsReservedFieldNumber ? "Field numbers 19,000 to 19,999 are reserved for the protobuf implementation" : null,
                }
                : new ReadingRow
                {
                    Label = "FIELD TAG",
                    Value = "Not a valid tag",
                    Detail = char.ToUpperInvariant(tag.Problem![0]) + tag.Problem[1..],
                };
            _readings.Add(row);
            Summary ??= row.Value;
            return row;
        }

        private void AddZigZagEncodedRow(long value)
        {
            ulong zz = ProtobufVarint.ZigZagEncode(value);
            _readings.Add(new ReadingRow
            {
                Label = value is >= int.MinValue and <= int.MaxValue ? "SINT32 / SINT64" : "SINT64",
                Value = HexText.Spaced(ProtobufVarint.Encode(zz)),
                IsMono = true,
                CopyText = HexText.Spaced(ProtobufVarint.Encode(zz)),
                Detail = $"ZigZag {NumberText.Grouped(zz)}, as a sint field writes it",
            });
        }

        private void AddUnsignedPatternRow(ulong pattern)
        {
            _readings.Add(new ReadingRow
            {
                Label = "UNSIGNED",
                Value = NumberText.Grouped(pattern),
                CopyText = NumberText.Plain(pattern),
                Detail = "The 64-bit pattern the varint holds",
            });
        }
    }
}
