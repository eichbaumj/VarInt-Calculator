using System;
using System.Collections.Generic;

namespace VarIntCalculator.Core
{
    /// <summary>The two varint encodings the calculator reads and writes.</summary>
    public enum VarintFormat
    {
        /// <summary>SQLite: big-endian base-128, 1 to 9 bytes, all 8 bits of a 9th byte used.</summary>
        Sqlite,

        /// <summary>Protocol Buffers: little-endian base-128 (LEB128), 1 to 10 bytes.</summary>
        Protobuf,
    }

    public enum DecodeStatus
    {
        /// <summary>A complete varint was read.</summary>
        Ok,

        /// <summary>No bytes were supplied.</summary>
        Empty,

        /// <summary>Every byte so far has its high bit set: the varint continues past the input.</summary>
        Incomplete,

        /// <summary>Protobuf only: ten bytes and none of them ends the varint.</summary>
        NoTerminator,
    }

    /// <summary>The result of reading one varint from the start of a byte sequence.</summary>
    public sealed record VarintDecode
    {
        public required VarintFormat Format { get; init; }
        public required DecodeStatus Status { get; init; }

        /// <summary>The varint's value as the unsigned 64-bit pattern it holds.</summary>
        public ulong Value { get; init; }

        /// <summary>Bytes the varint occupies (for Incomplete: every byte read).</summary>
        public int Length { get; init; }

        /// <summary>Bytes supplied.</summary>
        public int InputLength { get; init; }

        /// <summary>Bytes the shortest encoding of <see cref="Value"/> takes in this format.</summary>
        public int MinimalLength { get; init; }

        /// <summary>
        /// Protobuf only: the 10th byte carried bits past bit 63. They are dropped, which is
        /// how Google's protobuf runtime reads such a varint (verified against upb).
        /// </summary>
        public bool DroppedHighBits { get; init; }

        public bool IsOk => Status == DecodeStatus.Ok;
        public int TrailingBytes => IsOk ? InputLength - Length : 0;
        public bool IsMinimal => Length == MinimalLength;
    }

    /// <summary>
    /// SQLite varints, as sqlite3GetVarint / sqlite3PutVarint read and write them
    /// (https://www.sqlite.org/fileformat2.html#varint): big-endian groups of 7 bits, a set
    /// high bit meaning another byte follows, and a 9th byte whose 8 bits are all value.
    /// </summary>
    public static class SqliteVarint
    {
        public const int MaxLength = 9;

        public static VarintDecode Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                return new VarintDecode { Format = VarintFormat.Sqlite, Status = DecodeStatus.Empty };

            ulong value = 0;
            int limit = Math.Min(bytes.Length, MaxLength);
            for (int i = 0; i < limit; i++)
            {
                byte b = bytes[i];
                if (i == MaxLength - 1)
                    return Ok((value << 8) | b, MaxLength, bytes.Length);

                value = (value << 7) | (uint)(b & 0x7F);
                if (b < 0x80)
                    return Ok(value, i + 1, bytes.Length);
            }

            return new VarintDecode
            {
                Format = VarintFormat.Sqlite,
                Status = DecodeStatus.Incomplete,
                Length = bytes.Length,
                InputLength = bytes.Length,
            };
        }

        /// <summary>The bytes SQLite writes for <paramref name="value"/> (putVarint64).</summary>
        public static byte[] Encode(ulong value)
        {
            if ((value & 0xFF00_0000_0000_0000UL) != 0)
            {
                var nine = new byte[MaxLength];
                nine[8] = (byte)value;
                value >>= 8;
                for (int i = 7; i >= 0; i--)
                {
                    nine[i] = (byte)((value & 0x7F) | 0x80);
                    value >>= 7;
                }
                return nine;
            }

            Span<byte> reversed = stackalloc byte[MaxLength];
            int count = 0;
            do
            {
                reversed[count++] = (byte)((value & 0x7F) | 0x80);
                value >>= 7;
            }
            while (value != 0);
            reversed[0] &= 0x7F;

            var result = new byte[count];
            for (int i = 0; i < count; i++)
                result[i] = reversed[count - 1 - i];
            return result;
        }

        /// <summary>The bytes SQLite writes for a signed value, such as a negative rowid.</summary>
        public static byte[] Encode(long value) => Encode(unchecked((ulong)value));

        public static int EncodedLength(ulong value) => Encode(value).Length;

        private static VarintDecode Ok(ulong value, int length, int inputLength) => new()
        {
            Format = VarintFormat.Sqlite,
            Status = DecodeStatus.Ok,
            Value = value,
            Length = length,
            InputLength = inputLength,
            MinimalLength = EncodedLength(value),
        };
    }

    /// <summary>
    /// Protocol Buffers varints (https://protobuf.dev/programming-guides/encoding/#varints):
    /// little-endian groups of 7 bits, a set high bit meaning another byte follows, at most
    /// 10 bytes for a 64-bit value.
    /// </summary>
    public static class ProtobufVarint
    {
        public const int MaxLength = 10;

        public static VarintDecode Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                return new VarintDecode { Format = VarintFormat.Protobuf, Status = DecodeStatus.Empty };

            ulong value = 0;
            int limit = Math.Min(bytes.Length, MaxLength);
            for (int i = 0; i < limit; i++)
            {
                byte b = bytes[i];
                ulong group = (ulong)(b & 0x7F);
                value |= group << (7 * i);   // at the 10th byte only bit 0 still fits in 64 bits
                if (b < 0x80)
                {
                    return new VarintDecode
                    {
                        Format = VarintFormat.Protobuf,
                        Status = DecodeStatus.Ok,
                        Value = value,
                        Length = i + 1,
                        InputLength = bytes.Length,
                        MinimalLength = EncodedLength(value),
                        DroppedHighBits = i == MaxLength - 1 && group > 1,
                    };
                }
            }

            return new VarintDecode
            {
                Format = VarintFormat.Protobuf,
                Status = bytes.Length >= MaxLength ? DecodeStatus.NoTerminator : DecodeStatus.Incomplete,
                Length = limit,
                InputLength = bytes.Length,
            };
        }

        /// <summary>The bytes a protobuf encoder writes for an unsigned value (uint32, uint64).</summary>
        public static byte[] Encode(ulong value)
        {
            var bytes = new List<byte>(MaxLength);
            while (value >= 0x80)
            {
                bytes.Add((byte)(value | 0x80));
                value >>= 7;
            }
            bytes.Add((byte)value);
            return bytes.ToArray();
        }

        /// <summary>
        /// The bytes a protobuf encoder writes for an int32 or int64 field. A negative value is
        /// sign-extended to 64 bits, so it always takes 10 bytes.
        /// </summary>
        public static byte[] Encode(long value) => Encode(unchecked((ulong)value));

        public static int EncodedLength(ulong value)
        {
            int length = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                length++;
            }
            return length;
        }

        /// <summary>ZigZag, as sint32 and sint64 fields map signed values to unsigned ones.</summary>
        public static ulong ZigZagEncode(long value) => unchecked((ulong)((value << 1) ^ (value >> 63)));

        public static long ZigZagDecode(ulong value) => unchecked((long)(value >> 1) ^ -(long)(value & 1));

        /// <summary>A sint32 field decodes the low 32 bits.</summary>
        public static int ZigZagDecode32(ulong value)
        {
            uint n = unchecked((uint)value);
            return unchecked((int)(n >> 1) ^ -(int)(n & 1));
        }
    }

    /// <summary>A varint read as a protobuf field tag (key): field number and wire type.</summary>
    public readonly record struct TagReading(bool IsValid, ulong FieldNumber, int WireType, string? Problem)
    {
        public const ulong MaxFieldNumber = (1UL << 29) - 1;

        public static TagReading From(ulong value)
        {
            ulong field = value >> 3;
            int wireType = (int)(value & 7);
            if (value > uint.MaxValue)
                return new TagReading(false, field, wireType, "a tag is at most 32 bits");
            if (field == 0)
                return new TagReading(false, field, wireType, "field number 0 is not allowed");
            if (wireType > 5)
                return new TagReading(false, field, wireType, $"wire type {wireType} is not defined");
            return new TagReading(true, field, wireType, null);
        }

        public string WireTypeName => WireTypeNames.Name(WireType);

        /// <summary>Field numbers 19000 to 19999 are reserved for the protobuf implementation.</summary>
        public bool IsReservedFieldNumber => FieldNumber is >= 19000 and <= 19999;

        public ulong ToTag() => (FieldNumber << 3) | (uint)WireType;
    }

    public static class WireTypeNames
    {
        public static string Name(int wireType) => wireType switch
        {
            0 => "VARINT",
            1 => "I64",
            2 => "LEN",
            3 => "SGROUP",
            4 => "EGROUP",
            5 => "I32",
            _ => "undefined",
        };

        /// <summary>What follows a tag of this wire type on the wire.</summary>
        public static string Follows(int wireType) => wireType switch
        {
            0 => "A varint value follows",
            1 => "8 bytes follow (fixed64, sfixed64, double)",
            2 => "A varint length follows, then that many bytes",
            3 => "Group start (deprecated)",
            4 => "Group end (deprecated)",
            5 => "4 bytes follow (fixed32, sfixed32, float)",
            _ => "",
        };
    }
}
