using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VarIntCalculator
{
    public partial class MainWindow : Window
    {
        private List<string> history = new();
        private HistoryWindow? historyWindow;
        private TutorialWindow? tutorialWindow;

        public MainWindow()
        {
            InitializeComponent();
            
            // Set dark mode by default
            ApplyDarkMode();
            
            InputTextBox.Focus();
        }

        #region Button Click Handlers
        
        private void NumericButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                InputTextBox.Text += button.Content.ToString();
            }
        }

        private void HexButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                InputTextBox.Text += button.Content.ToString();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Clear();
            StatusText.Text = "Cleared input field";
        }

        private void VarIntButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (long.TryParse(InputTextBox.Text, out long decimalValue))
                {
                    string encodedValue = EncodeVarInt(decimalValue);
                    InputTextBox.Text = encodedValue;
                    StatusText.Text = "Converted to VarInt";
                    AddToHistory($"Decimal to VarInt: {decimalValue} -> {encodedValue}");
                }
                else
                {
                    StatusText.Text = "Error: Invalid Decimal Value";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void IntegerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string hexValue = CleanHexInput(InputTextBox.Text);
                if (!string.IsNullOrEmpty(hexValue))
                {
                    long decodedValue = DecodeVarInt(hexValue);
                    InputTextBox.Text = decodedValue.ToString();
                    StatusText.Text = "Converted to Integer";
                    AddToHistory($"Hex to Integer: {hexValue} -> {decodedValue}");
                }
                else
                {
                    StatusText.Text = "Error: Invalid Hex Value";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void LengthButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string hexValue = CleanHexInput(InputTextBox.Text);
                if (!string.IsNullOrEmpty(hexValue))
                {
                    long decodedValue = DecodeVarInt(hexValue);
                    
                    if (decodedValue >= 13 && decodedValue % 2 == 1)
                    {
                        int length = (int)((decodedValue - 13) / 2);
                        InputTextBox.Text = $"String Length: {length}";
                        StatusText.Text = "String length calculated";
                        AddToHistory($"String Length from VarInt: {hexValue} -> {length}");
                    }
                    else if (decodedValue >= 12 && decodedValue % 2 == 0)
                    {
                        int length = (int)((decodedValue - 12) / 2);
                        InputTextBox.Text = $"BLOB Length: {length}";
                        StatusText.Text = "BLOB length calculated";
                        AddToHistory($"BLOB Length from VarInt: {hexValue} -> {length}");
                    }
                    else
                    {
                        InputTextBox.Text = "Invalid for String/BLOB";
                        StatusText.Text = "Invalid length value for String/BLOB";
                    }
                }
                else
                {
                    StatusText.Text = "Error: Invalid Hex Value";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }
        
        #endregion

        #region Menu Handlers
        
        private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                bool isDarkMode = menuItem == DarkModeMenuItem;
                
                // Update menu check states
                DarkModeMenuItem.IsChecked = isDarkMode;
                ClassicMenuItem.IsChecked = !isDarkMode;

                // Apply the selected theme
                if (isDarkMode)
                {
                    ApplyDarkMode();
                }
                else
                {
                    ApplyClassicMode();
                }
            }
        }

        private void HistoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ToggleHistory();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void TutorialMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowTutorial();
        }
        
        #endregion

        #region Theme Management
        
        private void ApplyDarkMode()
        {
            // Apply dark theme to the main window
            Background = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x2e));
            InputTextBox.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            InputTextBox.Foreground = Brushes.White;

            // Update history window if open
            if (historyWindow != null && historyWindow.IsVisible)
            {
                historyWindow.ApplyDarkMode();
            }
        }

        private void ApplyClassicMode()
        {
            // Apply light theme to the main window
            Background = Brushes.WhiteSmoke;
            InputTextBox.Background = Brushes.White;
            InputTextBox.Foreground = Brushes.Black;

            // Update history window if open
            if (historyWindow != null && historyWindow.IsVisible)
            {
                historyWindow.ApplyClassicMode();
            }
        }
        
        #endregion

        #region History Management
        
        private void AddToHistory(string action)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            history.Add($"[{timestamp}] {action}");
            StatusText.Text = $"History updated - {history.Count} entries";
            
            // Update history window if it's open
            historyWindow?.UpdateHistory(history);
        }

        private void ToggleHistory()
        {
            if (historyWindow == null || !historyWindow.IsVisible)
            {
                historyWindow = new HistoryWindow(history);
                historyWindow.Owner = this;
                
                // Apply the current theme
                if (DarkModeMenuItem.IsChecked)
                {
                    historyWindow.ApplyDarkMode();
                }
                else
                {
                    historyWindow.ApplyClassicMode();
                }
                
                historyWindow.Show();
            }
            else
            {
                historyWindow.UpdateHistory(history);
                historyWindow.Activate();
            }
        }
        
        #endregion

        #region Tutorial Management
        
        private void ShowTutorial()
        {
            tutorialWindow = new TutorialWindow();
            tutorialWindow.Owner = this;
            tutorialWindow.ShowDialog();
        }
        
        #endregion

        #region VarInt Utility Methods
        
        private string CleanHexInput(string hexString)
        {
            return hexString.Replace(" ", "").Replace("0x", "");
        }

        private long DecodeVarInt(string hexString)
        {
            if (string.IsNullOrEmpty(hexString) || hexString.Length % 2 != 0)
            {
                throw new ArgumentException("Invalid hex value");
            }

            List<byte> bytes = new List<byte>();
            for (int i = 0; i < hexString.Length; i += 2)
            {
                string byteHex = hexString.Substring(i, 2);
                if (byte.TryParse(byteHex, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                {
                    bytes.Add(b);
                }
                else
                {
                    throw new ArgumentException($"Invalid hex byte: {byteHex}");
                }
            }

            long value = 0;
            foreach (byte b in bytes)
            {
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            return value;
        }

        private string EncodeVarInt(long value)
        {
            if (value < 0)
            {
                throw new ArgumentException("Value must be non-negative");
            }

            List<byte> result = new List<byte>();
            while (true)
            {
                byte byteVal = (byte)(value & 0x7F);
                value >>= 7;
                
                if (result.Count > 0)
                {
                    byteVal |= 0x80;
                }
                
                result.Insert(0, byteVal);
                
                if (value == 0)
                {
                    break;
                }
            }

            return string.Join("", result.Select(b => b.ToString("X2")));
        }
        
        #endregion
    }
} 