using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace VarIntCalculator
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow(List<string> historyEntries)
        {
            InitializeComponent();
            UpdateHistory(historyEntries);
        }

        public void UpdateHistory(List<string> historyEntries)
        {
            HistoryTextBox.Text = string.Join("\n", historyEntries);
            
            // Scroll to the end to show most recent entries
            HistoryTextBox.ScrollToEnd();
        }

        public void ApplyDarkMode()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x2e));
            HistoryTextBox.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            HistoryTextBox.Foreground = Brushes.White;
        }

        public void ApplyClassicMode()
        {
            Background = Brushes.WhiteSmoke;
            HistoryTextBox.Background = Brushes.White;
            HistoryTextBox.Foreground = Brushes.Black;
        }
    }
} 