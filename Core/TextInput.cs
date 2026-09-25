using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace VarIntCalculator.Core
{
    /// <summary>Hex digits typed or pasted into the calculator.</summary>
    public static class HexText
    {
        public static bool IsHexDigit(char c) =>
            c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

        /// <summary>
        /// Reads pasted text as hex bytes: "81 20", "0x81 0x20", "\x81\x20", "81:20" and "81-20"
        /// all give "8120". Returns null when anything other than hex digits and those
        /// separators is present, so a pasted sentence is refused rather than half-read.
        /// </summary>
        public static string? NormalizePasted(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var digits = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
                    || (c == '\\' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X')))
                {
                    i++;   // a 0x or \x prefix
                    continue;
                }
                if (IsHexDigit(c))
                    digits.Append(char.ToUpperInvariant(c));
                else if (!(char.IsWhiteSpace(c) || c is ',' or ':' or ';' or '-' or '_' or '|' or '.'))
                    return null;
            }
            return digits.Length == 0 ? null : digits.ToString();
        }

        /// <summary>The complete bytes in a run of hex digits; a final odd digit is left out.</summary>
        public static byte[] ToBytes(string digits)
        {
            var bytes = new byte[digits.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(digits.AsSpan(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        public static string Spaced(ReadOnlySpan<byte> bytes)
        {
            var sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static string Compact(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

        /// <summary>"8120F" as "81 20 F".</summary>
        public static string SpacedDigits(string digits)
        {
            var sb = new StringBuilder(digits.Length * 3 / 2);
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && i % 2 == 0)
                    sb.Append(' ');
                sb.Append(digits[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>Whole numbers typed or pasted in Encode mode.</summary>
    public static class DecimalText
    {
        public static readonly BigInteger MaxValue = ulong.MaxValue;
        public static readonly BigInteger MinValue = long.MinValue;

        /// <summary>Twenty digits hold 18,446,744,073,709,551,615.</summary>
        public const int MaxDigits = 20;

        public static bool TryParse(string input, out BigInteger value)
        {
            value = BigInteger.Zero;
            if (string.IsNullOrEmpty(input) || input == "-")
                return false;
            return BigInteger.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        public static bool IsInRange(BigInteger value) => value >= MinValue && value <= MaxValue;

        /// <summary>
        /// Reads pasted text as a whole number: digits with an optional leading minus sign,
        /// thousands separators (commas, spaces, underscores) allowed. Null when it is not one.
        /// </summary>
        public static string? NormalizePasted(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string t = text.Trim();
            var sb = new StringBuilder(t.Length);
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c is >= '0' and <= '9')
                    sb.Append(c);
                else if ((c == '-' || c == '−') && sb.Length == 0 && i == 0)
                    sb.Append('-');
                else if (!(c is ',' or '_' || char.IsWhiteSpace(c)))
                    return null;
            }
            string s = sb.ToString();
            if (s.Length == 0 || s == "-")
                return null;
            return Canonical(s);
        }

        /// <summary>Drops leading zeros; "-0" becomes "0".</summary>
        public static string Canonical(string input)
        {
            if (input == "-")
                return input;
            bool negative = input.StartsWith('-');
            string digits = (negative ? input[1..] : input).TrimStart('0');
            if (digits.Length == 0)
                return "0";
            return negative ? "-" + digits : digits;
        }

        /// <summary>"-1234567" as "-1,234,567"; a lone "-" stays as typed.</summary>
        public static string Grouped(string input)
        {
            if (string.IsNullOrEmpty(input) || input == "-")
                return input;
            bool negative = input.StartsWith('-');
            string digits = negative ? input[1..] : input;
            var sb = new StringBuilder(digits.Length + digits.Length / 3 + 1);
            for (int i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0)
                    sb.Append(',');
                sb.Append(digits[i]);
            }
            return negative ? "-" + sb : sb.ToString();
        }
    }

    public static class NumberText
    {
        private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

        public static string Grouped(ulong value) => value.ToString("N0", Us);

        public static string Grouped(long value) => value.ToString("N0", Us);

        public static string Grouped(int value) => value.ToString("N0", Us);

        public static string Grouped(BigInteger value) => value.ToString("N0", Us);

        public static string Plain(ulong value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Plain(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Bytes(ulong count) => count == 1 ? "1 byte" : Grouped(count) + " bytes";

        public static string Plural(ulong count, string singular, string plural) =>
            count == 1 ? "1 " + singular : Grouped(count) + " " + plural;
    }
}
