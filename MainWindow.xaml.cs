using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VarIntCalculator.Controls;
using VarIntCalculator.Core;
using VarIntCalculator.Services;

namespace VarIntCalculator
{
    public partial class MainWindow : Window
    {
        private enum PendingConfirm
        {
            None,
            ClearAll,
            DeleteSelected,
        }

        private readonly CalculatorModel _model = new();
        private readonly ObservableCollection<HistoryItemView> _history = new();
        private readonly Dictionary<char, Button> _keys = new();

        /// <summary>A result the display has held this long goes into the history.</summary>
        private readonly DispatcherTimer _historyTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

        private TutorialWindow? _tutorial;
        private bool _selectMode;
        private PendingConfirm _confirm;
        private bool _syncingSettings;

        public MainWindow()
        {
            InitializeComponent();

            AppSettings settings = App.Settings;
            _model.Page = settings.Page;
            _model.Load(settings.Format, settings.Direction, "");
            DataContext = _model;
            _model.PropertyChanged += (_, _) => OnModelChanged();

            foreach (HistoryEntry entry in SettingsStore.LoadHistory())
                _history.Add(new HistoryItemView(entry));
            HistoryList.ItemsSource = _history;

            foreach (Button key in Keypad.Children.OfType<Button>())
            {
                if (key.Tag is string t && t.Length == 1)
                    _keys[t[0]] = key;
            }

            BuildPageSizeChips();
            VersionText.Text = "Version " + App.Version;
            _historyTimer.Tick += (_, _) => AddToHistory(announce: false);
            _statusTimer.Tick += (_, _) => ShowIdle();
            ShowIdle();
            FitToWorkArea();

            Activated += (_, _) =>
            {
                if (Keyboard.FocusedElement is null)
                    Keyboard.Focus(this);
            };
            Closing += (_, _) => SaveOnExit();
        }

        // ================================================================ model

        private void OnModelChanged()
        {
            _historyTimer.Stop();
            if (_model.State == DisplayState.Valid)
                _historyTimer.Start();

            if (!_statusTimer.IsEnabled)
                ShowIdle();

            if (App.Settings.Format != _model.Format || App.Settings.Direction != _model.Direction)
            {
                App.Settings.Format = _model.Format;
                App.Settings.Direction = _model.Direction;
                SettingsStore.SaveSettings(App.Settings);
            }
        }

        /// <summary>Called by the tutorial's "Try it" links.</summary>
        public void LoadExample(VarintFormat format, string hex)
        {
            AddToHistory(announce: false);
            CloseSheets();
            _model.Load(format, CalcDirection.Decode, hex);
            Status("The tutorial's example is on the display");
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }

        // ================================================================ keyboard

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            base.OnPreviewTextInput(e);
            if (e.Handled || IsEditingText() || SheetHost.Visibility == Visibility.Visible)
                return;
            foreach (char c in e.Text)
                TypeChar(c, flash: true);
            e.Handled = true;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled)
                return;

            ModifierKeys mods = Keyboard.Modifiers;
            bool ctrl = mods == ModifierKeys.Control;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                if (SheetHost.Visibility == Visibility.Visible)
                    CloseSheets();
                else
                    ClearInput();
                e.Handled = true;
                return;
            }

            if (key == Key.H && ctrl)
            {
                if (HistorySheet.Visibility == Visibility.Visible)
                    CloseSheets();
                else
                    OpenHistory();
                e.Handled = true;
                return;
            }

            if (IsEditingText() || SheetHost.Visibility == Visibility.Visible)
                return;

            switch (key)
            {
                case Key.Back when mods == ModifierKeys.None:
                    DoBackspace(flash: true);
                    break;
                case Key.Delete when mods == ModifierKeys.None:
                    ClearInput();
                    break;
                case Key.Enter when mods == ModifierKeys.None:
                    AddToHistory(announce: true);
                    break;
                case Key.V when ctrl:
                case Key.Insert when mods == ModifierKeys.Shift:
                    PasteInput();
                    break;
                case Key.C when ctrl:
                    CopyPrimary();
                    break;
                case Key.D1 or Key.NumPad1 when ctrl:
                    _model.Format = VarintFormat.Sqlite;
                    break;
                case Key.D2 or Key.NumPad2 when ctrl:
                    _model.Format = VarintFormat.Protobuf;
                    break;
                case Key.D when ctrl:
                    _model.Direction = CalcDirection.Decode;
                    break;
                case Key.E when ctrl:
                    _model.Direction = CalcDirection.Encode;
                    break;
                case Key.F1:
                    OpenTutorial();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private static bool IsEditingText() => Keyboard.FocusedElement is TextBox;

        private void TypeChar(char c, bool flash)
        {
            if (c is '-' or '−')
            {
                if (_model.ToggleSign())
                {
                    if (flash)
                        KeyAssist.Flash(SignKey);
                }
                else if (_model.IsDecode)
                {
                    Status("The sign applies in Encode mode");
                }
                return;
            }

            char upper = char.ToUpperInvariant(c);
            if (_model.Append(c))
            {
                if (flash && _keys.TryGetValue(upper, out Button? button))
                    KeyAssist.Flash(button);
                return;
            }

            if (_model.IsDecode && HexText.IsHexDigit(c))
                Status("The display holds 10 bytes, the longest varint");
            else if (_model.IsEncode && char.IsAsciiDigit(c))
                Status("The display holds 20 digits, enough for any 64-bit value");
            else if (_model.IsEncode && HexText.IsHexDigit(c))
                Status("Encode takes a decimal number. Switch to Decode to type hex bytes");
        }

        // ================================================================ keypad

        private void Key_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string tag } && tag.Length == 1)
                TypeChar(tag[0], flash: false);
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => ClearInput();

        private void Sign_Click(object sender, RoutedEventArgs e) => TypeChar('-', flash: false);

        private void Backspace_Click(object sender, RoutedEventArgs e) => DoBackspace(flash: false);

        private void ClearInput()
        {
            AddToHistory(announce: false);   // a result on the display is kept before it goes
            if (_model.Clear())
                KeyAssist.Flash(ClearKey);
        }

        private void DoBackspace(bool flash)
        {
            if (_model.Backspace() && flash)
                KeyAssist.Flash(BackspaceKey);
        }

        // ================================================================ clipboard

        private void CopyPrimary()
        {
            if (_model.State == DisplayState.Valid)
                CopyText(_model.PrimaryCopyText);
            else
                Status("Nothing to copy yet");
        }

        private void CopyPrimary_Click(object sender, RoutedEventArgs e) => CopyPrimary();

        private void CopyInput_Click(object sender, RoutedEventArgs e) => CopyText(_model.InputCopyText);

        private void CopyReading_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: ReadingRow { CopyText: { } text } })
                CopyText(text);
        }

        private void CopyText(string text)
        {
            if (ClipboardText.TrySet(text))
            {
                Status("Copied " + text);
                AddToHistory(announce: false);
            }
            else
            {
                Status("The clipboard is busy. Try again");
            }
        }

        private void PasteInput()
        {
            string? text = ClipboardText.TryGet();
            if (string.IsNullOrWhiteSpace(text))
            {
                Status("The clipboard holds no text");
                return;
            }

            switch (_model.Paste(text))
            {
                case PasteOutcome.Pasted:
                    Status("Pasted");
                    break;
                case PasteOutcome.Truncated:
                    Status("Pasted the first 10 bytes: a varint is at most 10 bytes long");
                    break;
                default:
                    Status(_model.IsDecode ? "The clipboard does not hold hex bytes" : "The clipboard does not hold a whole number");
                    break;
            }
        }

        // ================================================================ status line

        private void Status(string message)
        {
            StatusText.Text = message;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void ShowIdle()
        {
            _statusTimer.Stop();
            StatusText.Text = _model.IdleHint;
        }

        // ================================================================ history

        private void AddToHistory(bool announce)
        {
            _historyTimer.Stop();
            HistoryEntry? entry = _model.Snapshot();
            if (entry is null)
            {
                if (announce)
                    Status("Nothing to add: the display holds no result");
                return;
            }
            if (_history.Count > 0 && _history[0].Entry.SameCalculation(entry))
            {
                if (announce)
                    Status("Already in the history");
                return;
            }

            // An older copy of the same calculation moves up rather than repeating.
            HistoryItemView? older = _history.FirstOrDefault(h => h.Entry.SameCalculation(entry));
            if (older is not null)
                _history.Remove(older);
            _history.Insert(0, new HistoryItemView(entry) { SelectMode = _selectMode });
            while (_history.Count > SettingsStore.MaxHistory)
                _history.RemoveAt(_history.Count - 1);
            SettingsStore.SaveHistory(_history.Select(h => h.Entry));
            RefreshHistoryFooter();
            if (announce)
                Status("Added to the history");
        }

        private void History_Click(object sender, RoutedEventArgs e) => OpenHistory();

        private void OpenHistory()
        {
            AddToHistory(announce: false);
            SetSelectMode(false);
            OpenSheet(HistorySheet);
        }

        private void HistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: HistoryItemView item })
                return;

            if (_selectMode)
            {
                item.IsSelected = !item.IsSelected;
                _confirm = PendingConfirm.None;
                RefreshHistoryFooter();
                return;
            }

            CloseSheets();
            _model.Load(item.Entry);
            _historyTimer.Stop();
            Status("Restored from the history");
        }

        private void HistorySelectMode_Click(object sender, RoutedEventArgs e) => SetSelectMode(!_selectMode);

        private void SetSelectMode(bool on)
        {
            _selectMode = on;
            _confirm = PendingConfirm.None;
            foreach (HistoryItemView item in _history)
            {
                item.SelectMode = on;
                if (!on)
                    item.IsSelected = false;
            }
            RefreshHistoryFooter();
        }

        private void HistorySelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool all = _history.Count > 0 && _history.All(h => h.IsSelected);
            foreach (HistoryItemView item in _history)
                item.IsSelected = !all;
            _confirm = PendingConfirm.None;
            RefreshHistoryFooter();
        }

        private void HistoryDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            int selected = _history.Count(h => h.IsSelected);
            if (selected == 0)
                return;
            _confirm = PendingConfirm.DeleteSelected;
            HistoryConfirmText.Text = selected == 1 ? "Delete 1 calculation?" : $"Delete {selected} calculations?";
            HistoryConfirmButton.Content = "Delete";
            RefreshHistoryFooter();
        }

        private void HistoryClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_history.Count == 0)
                return;
            _confirm = PendingConfirm.ClearAll;
            HistoryConfirmText.Text = _history.Count == 1 ? "Clear the 1 calculation?" : $"Clear all {_history.Count} calculations?";
            HistoryConfirmButton.Content = "Clear all";
            RefreshHistoryFooter();
        }

        private void HistoryConfirm_Click(object sender, RoutedEventArgs e)
        {
            int removed;
            if (_confirm == PendingConfirm.ClearAll)
            {
                removed = _history.Count;
                _history.Clear();
            }
            else
            {
                var doomed = _history.Where(h => h.IsSelected).ToList();
                removed = doomed.Count;
                foreach (HistoryItemView item in doomed)
                    _history.Remove(item);
            }

            SettingsStore.SaveHistory(_history.Select(h => h.Entry));
            SetSelectMode(false);
            Status(removed == 1 ? "Deleted 1 calculation" : $"Deleted {removed} calculations");
        }

        private void HistoryConfirmCancel_Click(object sender, RoutedEventArgs e)
        {
            _confirm = PendingConfirm.None;
            RefreshHistoryFooter();
        }

        private void RefreshHistoryFooter()
        {
            int count = _history.Count;
            int selected = _history.Count(h => h.IsSelected);

            HistoryEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryCountText.Text = count == 1 ? "1 calculation" : $"{count} calculations";
            ClearAllButton.IsEnabled = count > 0;
            SelectModeButton.IsEnabled = count > 0 || _selectMode;
            SelectModeButton.Content = _selectMode ? "Done" : "Select";
            HistorySelectedText.Text = selected == 0 ? "Choose calculations to delete"
                : selected == 1 ? "1 selected" : $"{selected} selected";
            DeleteSelectedButton.IsEnabled = selected > 0;
            SelectAllButton.Content = count > 0 && selected == count ? "Select none" : "Select all";

            HistoryBrowseFooter.Visibility = !_selectMode && _confirm == PendingConfirm.None ? Visibility.Visible : Visibility.Collapsed;
            HistorySelectFooter.Visibility = _selectMode && _confirm == PendingConfirm.None ? Visibility.Visible : Visibility.Collapsed;
            HistoryConfirmFooter.Visibility = _confirm != PendingConfirm.None ? Visibility.Visible : Visibility.Collapsed;
        }

        // ================================================================ settings

        private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void ChangePage_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void BuildPageSizeChips()
        {
            foreach (int size in PageGeometry.PageSizes)
            {
                var chip = new RadioButton
                {
                    Style = (Style)FindResource("Chip"),
                    Content = NumberText.Grouped(size),
                    Tag = size,
                    GroupName = "PageSize",
                    ToolTip = size == PageGeometry.DefaultPageSize ? "SQLite's default page size" : null,
                };
                System.Windows.Automation.AutomationProperties.SetName(chip, $"Page size {size} bytes");
                chip.Checked += PageSize_Checked;
                PageSizeChips.Children.Add(chip);
            }
        }

        private void OpenSettings()
        {
            _syncingSettings = true;
            PageGeometry page = _model.Page;
            foreach (RadioButton chip in PageSizeChips.Children.OfType<RadioButton>())
                chip.IsChecked = (int)chip.Tag == page.PageSize;
            ReservedBox.Text = page.ReservedBytes.ToString(CultureInfo.InvariantCulture);
            ReservedError.Visibility = Visibility.Collapsed;
            TableLeafOption.IsChecked = page.Kind == BTreeKind.TableLeaf;
            IndexOption.IsChecked = page.Kind == BTreeKind.Index;
            ThemeDefaultOption.IsChecked = App.Settings.Theme == AppTheme.Default;
            ThemeDarkOption.IsChecked = App.Settings.Theme == AppTheme.Dark;
            ThemeLightOption.IsChecked = App.Settings.Theme == AppTheme.Light;
            _syncingSettings = false;

            UpdatePageFormula();
            OpenSheet(SettingsSheet);
        }

        private void PageSize_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncingSettings || sender is not RadioButton { Tag: int size })
                return;

            PageGeometry page = _model.Page;
            int reserved = page.ReservedBytes;
            int max = PageGeometry.MaxReservedFor(size);
            if (reserved > max)
            {
                reserved = max;
                _syncingSettings = true;
                ReservedBox.Text = reserved.ToString(CultureInfo.InvariantCulture);
                _syncingSettings = false;
                Status($"Reserved bytes lowered to {reserved}: a {NumberText.Grouped(size)}-byte page keeps at least 480 usable bytes");
            }
            ReservedError.Visibility = Visibility.Collapsed;
            ApplyPage(page with { PageSize = size, ReservedBytes = reserved });
        }

        private void ReservedBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
            e.Handled = !e.Text.All(char.IsAsciiDigit);

        private void ReservedBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingSettings)
                return;

            PageGeometry page = _model.Page;
            int max = PageGeometry.MaxReservedFor(page.PageSize);
            if (ReservedBox.Text.Length == 0)
            {
                ReservedError.Visibility = Visibility.Collapsed;
                return;
            }
            if (int.TryParse(ReservedBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int reserved) && reserved <= max)
            {
                ReservedError.Visibility = Visibility.Collapsed;
                ApplyPage(page with { ReservedBytes = reserved });
                return;
            }
            ReservedError.Text = $"0 to {max} on a {NumberText.Grouped(page.PageSize)}-byte page: SQLite needs at least 480 usable bytes";
            ReservedError.Visibility = Visibility.Visible;
        }

        private void BTree_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncingSettings)
                return;
            ApplyPage(_model.Page with { Kind = IndexOption.IsChecked == true ? BTreeKind.Index : BTreeKind.TableLeaf });
        }

        private void ApplyPage(PageGeometry page)
        {
            if (!page.IsValid)
                return;
            _model.Page = page;
            App.Settings.PageSize = page.PageSize;
            App.Settings.ReservedBytes = page.ReservedBytes;
            App.Settings.BTree = page.Kind;
            SettingsStore.SaveSettings(App.Settings);
            UpdatePageFormula();
        }

        private void UpdatePageFormula()
        {
            PageGeometry page = _model.Page;
            static string N(int n) => NumberText.Grouped(n).PadLeft(6);
            PageFormulaText.Text =
                $"U {N(page.UsableSize)}  usable bytes per page\n" +
                $"X {N(page.MaxLocal)}  most payload kept on the page\n" +
                $"M {N(page.MinLocal)}  least kept when it spills\n" +
                $"  {N(page.OverflowPageCapacity)}  bytes per overflow page";
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncingSettings || sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out AppTheme theme))
                return;
            App.ApplyTheme(theme);
            App.Settings.Theme = theme;
            SettingsStore.SaveSettings(App.Settings);
        }

        // ================================================================ sheets

        private void OpenSheet(FrameworkElement sheet)
        {
            RefreshHistoryFooter();
            SheetHost.Visibility = Visibility.Visible;
            HistorySheet.Visibility = sheet == HistorySheet ? Visibility.Visible : Visibility.Collapsed;
            SettingsSheet.Visibility = sheet == SettingsSheet ? Visibility.Visible : Visibility.Collapsed;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (sheet.RenderTransform is TranslateTransform slide)
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        private void CloseSheets()
        {
            SheetHost.Visibility = Visibility.Collapsed;
            HistorySheet.Visibility = Visibility.Collapsed;
            SettingsSheet.Visibility = Visibility.Collapsed;
            if (_selectMode || _confirm != PendingConfirm.None)
                SetSelectMode(false);
            Keyboard.Focus(this);
        }

        private void CloseSheet_Click(object sender, RoutedEventArgs e) => CloseSheets();

        private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseSheets();

        // ================================================================ windows

        private void Tutorial_Click(object sender, RoutedEventArgs e) => OpenTutorial();

        private void OpenTutorial()
        {
            if (_tutorial is not null)
            {
                if (_tutorial.WindowState == WindowState.Minimized)
                    _tutorial.WindowState = WindowState.Normal;
                _tutorial.Activate();
                return;
            }
            _tutorial = new TutorialWindow(this) { Owner = this };
            _tutorial.Closed += (_, _) => _tutorial = null;
            _tutorial.Show();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Keeps the window on a short screen: the display shrinks and scrolls, the keys do not.</summary>
        private void FitToWorkArea()
        {
            Rect work = SystemParameters.WorkArea;
            double room = work.Height - 16;
            if (MinHeight > room)
                MinHeight = Math.Max(480, room);
            if (Height > room)
                Height = Math.Max(MinHeight, room);
        }

        private void SaveOnExit()
        {
            AddToHistory(announce: false);
            SettingsStore.SaveSettings(App.Settings);
        }
    }
}
