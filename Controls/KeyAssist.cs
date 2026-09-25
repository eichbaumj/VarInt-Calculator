using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace VarIntCalculator.Controls
{
    /// <summary>
    /// Per-style colors for the keypad's one key template, and the flash a key gives when its
    /// character is typed on the keyboard, so the keypad answers the keyboard the way a real
    /// calculator's display and keys do.
    /// </summary>
    public static class KeyAssist
    {
        public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
            "HoverBackground", typeof(Brush), typeof(KeyAssist), new PropertyMetadata(null));

        public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
            "PressedBackground", typeof(Brush), typeof(KeyAssist), new PropertyMetadata(null));

        public static readonly DependencyProperty IsFlashingProperty = DependencyProperty.RegisterAttached(
            "IsFlashing", typeof(bool), typeof(KeyAssist), new PropertyMetadata(false));

        public static Brush GetHoverBackground(DependencyObject d) => (Brush)d.GetValue(HoverBackgroundProperty);

        public static void SetHoverBackground(DependencyObject d, Brush value) => d.SetValue(HoverBackgroundProperty, value);

        public static Brush GetPressedBackground(DependencyObject d) => (Brush)d.GetValue(PressedBackgroundProperty);

        public static void SetPressedBackground(DependencyObject d, Brush value) => d.SetValue(PressedBackgroundProperty, value);

        public static bool GetIsFlashing(DependencyObject d) => (bool)d.GetValue(IsFlashingProperty);

        public static void SetIsFlashing(DependencyObject d, bool value) => d.SetValue(IsFlashingProperty, value);

        /// <summary>Shows the key pressed for a moment.</summary>
        public static void Flash(UIElement key)
        {
            SetIsFlashing(key, true);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                SetIsFlashing(key, false);
            };
            timer.Start();
        }
    }
}
