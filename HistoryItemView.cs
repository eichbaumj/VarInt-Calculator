using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using VarIntCalculator.Core;

namespace VarIntCalculator
{
    /// <summary>A history entry as the History sheet shows it.</summary>
    public sealed class HistoryItemView : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _selectMode;

        public HistoryItemView(HistoryEntry entry)
        {
            Entry = entry;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public HistoryEntry Entry { get; }

        public string InputDisplay => Entry.InputDisplay;

        public string Result => Entry.Result;

        public bool ResultIsBytes => Entry.Direction == CalcDirection.Encode;

        public string ModeText =>
            (Entry.Format == VarintFormat.Sqlite ? "SQLite" : "Protobuf") + " · " +
            (Entry.Direction == CalcDirection.Decode ? "Decode" : "Encode");

        public string SummaryText => string.IsNullOrEmpty(Entry.Summary) ? "" : " · " + Entry.Summary;

        public string TimeText
        {
            get
            {
                DateTime local = Entry.TimestampUtc.ToLocalTime();
                DateTime now = DateTime.Now;
                if (local.Date == now.Date)
                    return local.ToString("t", CultureInfo.CurrentCulture);
                if ((now.Date - local.Date).TotalDays < 7)
                    return local.ToString("ddd ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
                return local.ToString("MMM d", CultureInfo.CurrentCulture);
            }
        }

        public string AutomationName => $"{InputDisplay} gives {Result}, {ModeText}";

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        public bool SelectMode
        {
            get => _selectMode;
            set
            {
                if (Set(ref _selectMode, value))
                    OnPropertyChanged(nameof(BrowseMode));
            }
        }

        public bool BrowseMode => !_selectMode;

        private bool Set(ref bool field, bool value, [CallerMemberName] string? name = null)
        {
            if (field == value)
                return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
