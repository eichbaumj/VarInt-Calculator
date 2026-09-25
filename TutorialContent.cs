using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using VarIntCalculator.Controls;
using VarIntCalculator.Core;

namespace VarIntCalculator
{
    public sealed record TutorialStep(string Title, Func<FrameworkElement> Body);

    /// <summary>
    /// The tutorial's steps: the Apple version's walk through one varint (continuation bit,
    /// binary, seven-bit groups, base 128, practice), plus SQLite's ninth byte, protobuf's
    /// little-endian order and the local payload. Every worked number is computed from the
    /// bytes shown, and the tests check the ones quoted in the text.
    /// </summary>
    public static class TutorialContent
    {
        public static IReadOnlyList<TutorialStep> Build(Action<VarintFormat, string> tryIt) => new List<TutorialStep>
        {
            new("What a varint is", () => Stack(
                Lead("A varint stores a whole number in as few bytes as it needs: small numbers take one byte, large ones take more."),
                Para("SQLite writes varints for payload sizes, rowids, record header sizes and serial types. Protocol Buffers writes them for field tags, lengths and integer fields."),
                Para("Each byte carries seven bits of the number. Its eighth bit, the high bit, says whether another byte follows. The next steps decode two bytes by hand, the way the calculator does."))),

            new("The continuation bit", () => Stack(
                Lead("The high bit of each byte says whether the varint goes on."),
                Para("In hex you can read it from the first digit: 8 to F means the high bit is set and another byte follows; 0 to 7 means this byte is the last one."),
                Row(ByteCard(0x81), ByteCard(0x4D)),
                Para("So 81 4D is one varint, two bytes long."))),

            new("Converting to binary", () => Stack(
                Lead("Write each byte as eight bits. The first bit is the high bit; the other seven carry the number."),
                BinaryCard(0x81),
                BinaryCard(0x4D))),

            new("Keeping the seven value bits", () => Stack(
                Lead("Drop the high bit of each byte and keep its seven value bits."),
                Card(
                    MonoLine(("81   ", null), ("1", "ErrorTextBrush"), (" 0000001  →  0000001 = 1", null)),
                    MonoLine(("4D   ", null), ("0", "ValidBrush"), (" 1001101  →  1001101 = 77", null))),
                Para("These groups, 1 and 77, are the digits of the number in base 128. The order they go in depends on the format."))),

            new("SQLite reads big-endian", () => Stack(
                Lead("SQLite puts the most significant group first. Multiply each group by 128 raised to its place, counting from the last byte."),
                SumCard(new byte[] { 0x81, 0x4D }, littleEndian: false),
                TryIt(tryIt, VarintFormat.Sqlite, "814D"))),

            new("SQLite's ninth byte", () => Stack(
                Lead("A SQLite varint is never longer than nine bytes."),
                Para("When the first eight bytes all have the high bit set, the ninth byte is not split: all eight of its bits are value bits. That is how nine bytes hold a full 64-bit number (8 × 7 + 8 = 64)."),
                Card(
                    Text("FF FF FF FF FF FF FF FF 7F", mono: true, size: 15),
                    Text(NumberText.Grouped(SqliteVarint.Decode(Convert.FromHexString("FFFFFFFFFFFFFFFF7F")).Value), size: 17, weight: FontWeights.SemiBold, margin: new Thickness(0, 6, 0, 0)),
                    Text("As a signed rowid: " + NumberText.Grouped(unchecked((long)SqliteVarint.Decode(Convert.FromHexString("FFFFFFFFFFFFFFFF7F")).Value)), brush: "SecondaryTextBrush", margin: new Thickness(0, 2, 0, 0))),
                Para("SQLite writes rowid -129 exactly this way. Read the 7F as seven bits instead and you get a different, wrong number."),
                TryIt(tryIt, VarintFormat.Sqlite, "FFFFFFFFFFFFFFFF7F"))),

            new("Protobuf reads little-endian", () => Stack(
                Lead("Protocol Buffers uses the same seven-bit groups in the opposite order: the first byte holds the least significant group."),
                SumCard(new byte[] { 0xAC, 0x02 }, littleEndian: true),
                Para("So the same bytes give different numbers in the two formats: 81 4D is 205 as a SQLite varint and 9,857 as a protobuf varint. Switch the calculator between SQLite and Protobuf to see both."),
                Para("A protobuf varint takes up to ten bytes. A negative int32 or int64 is sign-extended to ten bytes; sint32 and sint64 fields use ZigZag instead, so small negative numbers stay short."),
                TryIt(tryIt, VarintFormat.Protobuf, "AC02"))),

            new("Practice", () => Stack(
                Lead("Decode FF FF 81 05 as a SQLite varint. Which bytes have the high bit set?"),
                Row(ByteCard(0xFF, compact: true), ByteCard(0xFF, compact: true), ByteCard(0x81, compact: true), ByteCard(0x05, compact: true)),
                SumCard(new byte[] { 0xFF, 0xFF, 0x81, 0x05 }, littleEndian: false),
                TryIt(tryIt, VarintFormat.Sqlite, "FFFF8105"))),

            new("Local payload and overflow", () => Stack(
                Lead("In a table b-tree cell, the first varint is the payload size, P. When P is too big for the page, SQLite keeps part of the payload on the page, the local payload, and moves the rest to a chain of overflow pages."),
                Card(
                    MonoLine(("U  ", "AccentTextBrush"), ("usable page size: page size minus reserved bytes", null)),
                    MonoLine(("X  ", "AccentTextBrush"), ("U - 35 on a table leaf page", null)),
                    MonoLine(("M  ", "AccentTextBrush"), ("((U - 12) × 32 / 255) - 23", null)),
                    MonoLine(("K  ", "AccentTextBrush"), ("M + ((P - M) mod (U - 4))", null)),
                    Text("Local payload = P if P ≤ X, otherwise K if K ≤ X, otherwise M.", margin: new Thickness(0, 8, 0, 0))),
                Para(WorkedExample()),
                Para("An index b-tree cell has a smaller X: ((U - 12) × 64 / 255) - 23. Set the page size, reserved bytes and b-tree type in Settings."),
                TryIt(tryIt, VarintFormat.Sqlite, "A708"))),

            new("That is the whole trick", () => Stack(
                Lead("You can read a varint by hand now."),
                Card(
                    Check("The high bit says whether another byte follows"),
                    Check("Each byte carries seven value bits"),
                    Check("SQLite reads the groups big-endian, with a full ninth byte"),
                    Check("Protobuf reads them little-endian, in up to ten bytes"),
                    Check("The payload size tells you how much of a cell stays on its page")),
                Para("The calculator shows these readings for every value, so you can check your own work."))),
        };

        private static string WorkedExample()
        {
            var page = PageGeometry.Default;
            var split = SqlitePayload.Split(5000, page);
            ulong k = (ulong)page.MinLocal + (5000UL - (ulong)page.MinLocal) % (ulong)page.OverflowPageCapacity;
            return $"For example, P = 5,000 on a 4,096-byte page: X = {NumberText.Grouped(page.MaxLocal)} and M = {NumberText.Grouped(page.MinLocal)}, " +
                   $"so K = {NumberText.Grouped(page.MinLocal)} + ({NumberText.Grouped(5000 - page.MinLocal)} mod {NumberText.Grouped(page.OverflowPageCapacity)}) = {NumberText.Grouped(k)}. " +
                   $"The cell keeps {NumberText.Grouped(split.Local)} bytes, then the 4-byte number of the first overflow page; " +
                   $"the other {NumberText.Grouped(split.Spilled)} bytes fill {NumberText.Plural(split.OverflowPages, "overflow page", "overflow pages")}.";
        }

        // ================================================================ building blocks

        private static StackPanel Stack(params UIElement[] children)
        {
            var panel = new StackPanel();
            foreach (UIElement child in children)
                panel.Children.Add(child);
            return panel;
        }

        private static TextBlock Text(string text, bool mono = false, double size = 14, FontWeight? weight = null,
            string brush = "PrimaryTextBrush", Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                Margin = margin ?? new Thickness(0),
            };
            if (mono)
                tb.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return tb;
        }

        private static TextBlock Lead(string text) =>
            Text(text, size: 15.5, margin: new Thickness(0, 0, 0, 10));

        private static TextBlock Para(string text)
        {
            TextBlock tb = Text(text, size: 14, brush: "SecondaryTextBrush", margin: new Thickness(0, 6, 0, 6));
            tb.LineHeight = 21;
            return tb;
        }

        private static Border Card(params UIElement[] children)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 8, 0, 8),
                Child = Stack(children),
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return card;
        }

        private static WrapPanel Row(params FrameworkElement[] cards)
        {
            var row = new WrapPanel();
            foreach (FrameworkElement card in cards)
            {
                card.Margin = new Thickness(0, 8, 12, 8);
                row.Children.Add(card);
            }
            return row;
        }

        private static TextBlock Kicker(string text)
        {
            var tb = new TextBlock { Text = Tracking.Apply(text), Margin = new Thickness(0, 0, 0, 6) };
            tb.SetResourceReference(FrameworkElement.StyleProperty, "Kicker");
            return tb;
        }

        /// <summary>A line of monospaced runs, each optionally in a theme brush.</summary>
        private static TextBlock MonoLine(params (string Text, string? Brush)[] runs)
        {
            var tb = new TextBlock { FontSize = 14, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            tb.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            foreach ((string text, string? brush) in runs)
            {
                var run = new Run(text);
                if (brush is not null)
                {
                    run.SetResourceReference(TextElement.ForegroundProperty, brush);
                    run.FontWeight = FontWeights.Bold;
                }
                tb.Inlines.Add(run);
            }
            return tb;
        }

        private static FrameworkElement Check(string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var icon = new PackIcon { Kind = PackIconKind.CheckCircleOutline, Width = 17, Height = 17, Margin = new Thickness(0, 1, 9, 0) };
            icon.SetResourceReference(Control.ForegroundProperty, "ValidBrush");
            row.Children.Add(icon);
            row.Children.Add(Text(text, size: 14));
            return row;
        }

        /// <summary>A byte as two hex digits; the first digit shows the high bit.</summary>
        private static FrameworkElement ByteCard(byte value, bool compact = false)
        {
            bool continues = value >= 0x80;
            string hex = value.ToString("X2", CultureInfo.InvariantCulture);

            var digits = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            digits.Children.Add(Nibble(hex[0], highlight: continues ? "ErrorBrush" : "ValidBrush", left: true));
            digits.Children.Add(Nibble(hex[1], highlight: null, left: false));

            var panel = new StackPanel();
            panel.Children.Add(Kicker("BYTE " + hex));
            panel.Children.Add(digits);
            if (!compact)
            {
                TextBlock verdict = Text(continues ? "High bit set: another byte follows" : "High bit clear: the last byte",
                    size: 12.5, brush: continues ? "ErrorTextBrush" : "ValidBrush", margin: new Thickness(0, 8, 0, 0));
                verdict.FontWeight = FontWeights.SemiBold;
                panel.Children.Add(verdict);
            }

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 12),
                MinWidth = compact ? 0 : 200,
                Child = panel,
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return card;
        }

        private static Border Nibble(char digit, string? highlight, bool left)
        {
            var text = new TextBlock
            {
                Text = digit.ToString(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");

            var box = new Border
            {
                Width = 40,
                Height = 42,
                BorderThickness = new Thickness(1.5),
                CornerRadius = left ? new CornerRadius(6, 0, 0, 6) : new CornerRadius(0, 6, 6, 0),
                Margin = new Thickness(left ? 0 : -1.5, 0, 0, 0),
                Child = text,
            };
            box.SetResourceReference(Border.BackgroundProperty, "DisplayBackgroundBrush");
            box.SetResourceReference(Border.BorderBrushProperty, highlight ?? "CardBorderBrush");
            return box;
        }

        /// <summary>A byte in binary: the high bit in red, the seven value bits in green.</summary>
        private static FrameworkElement BinaryCard(byte value)
        {
            string hex = value.ToString("X2", CultureInfo.InvariantCulture);
            string bits = Convert.ToString(value, 2).PadLeft(8, '0');
            int group = value & 0x7F;
            return Card(
                Kicker("BYTE " + hex),
                MonoLine(("binary   ", "SecondaryTextBrush"), (bits[..1], value >= 0x80 ? "ErrorTextBrush" : "ValidBrush"), (" ", null), (bits[1..], "ValidBrush")),
                MonoLine(("high bit ", "SecondaryTextBrush"), (bits[..1] + (value >= 0x80 ? "  more bytes follow" : "  last byte"), null)),
                MonoLine(("value    ", "SecondaryTextBrush"), (bits[1..] + " = " + group.ToString(CultureInfo.InvariantCulture), null)));
        }

        /// <summary>The base-128 sum of a varint's groups, in the order the format reads them.</summary>
        private static FrameworkElement SumCard(byte[] bytes, bool littleEndian)
        {
            var lines = new List<UIElement> { Kicker(littleEndian ? "LITTLE-ENDIAN: FIRST BYTE IS THE LOW GROUP" : "BIG-ENDIAN: FIRST BYTE IS THE HIGH GROUP") };
            System.Numerics.BigInteger total = 0;
            var terms = new List<string>();
            for (int i = 0; i < bytes.Length; i++)
            {
                int power = littleEndian ? i : bytes.Length - 1 - i;
                int group = bytes[i] & 0x7F;
                System.Numerics.BigInteger contribution = group * System.Numerics.BigInteger.Pow(128, power);
                total += contribution;
                terms.Add(NumberText.Grouped(contribution));
                lines.Add(MonoLine(
                    (bytes[i].ToString("X2", CultureInfo.InvariantCulture), "AccentTextBrush"),
                    ($"  {group,3} × 128^{power} = {NumberText.Grouped(contribution)}", null)));
            }
            TextBlock sum = MonoLine(("= ", "AccentTextBrush"), (string.Join(" + ", terms) + " = ", null), (NumberText.Grouped(total), "ValidBrush"));
            sum.Margin = new Thickness(0, 8, 0, 0);
            lines.Add(sum);
            return Card(lines.ToArray());
        }

        private static FrameworkElement TryIt(Action<VarintFormat, string> tryIt, VarintFormat format, string hex)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new PackIcon { Kind = PackIconKind.Calculator, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock
            {
                Text = $"Try {HexText.SpacedDigits(hex)} on the calculator ({(format == VarintFormat.Sqlite ? "SQLite" : "Protobuf")})",
                VerticalAlignment = VerticalAlignment.Center,
            });
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 6, 0, 0) };
            button.SetResourceReference(FrameworkElement.StyleProperty, "LinkButton");
            button.Click += (_, _) => tryIt(format, hex);
            return button;
        }
    }
}
