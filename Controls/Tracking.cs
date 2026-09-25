using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VarIntCalculator.Controls
{
    /// <summary>
    /// Letter-spacing for the uppercase Saira labels, as Firefly sets them (WPF has no tracking
    /// property): a thin space (U+2009) goes between every character. Decorative labels only.
    /// <c>&lt;TextBlock controls:Tracking.Text="HISTORY"/&gt;</c> for literal text,
    /// <see cref="TrackingConverter"/> for bound text.
    /// </summary>
    public static class Tracking
    {
        private const string ThinSpace = " ";

        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(Tracking), new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

        public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock tb)
                tb.Text = Apply(e.NewValue as string);
        }

        public static string Apply(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.Length == 1)
                return text ?? "";
            return string.Join(ThinSpace, text.Select(c => c.ToString()));
        }
    }

    public sealed class TrackingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Tracking.Apply(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
