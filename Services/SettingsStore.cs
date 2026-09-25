using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VarIntCalculator.Core;

namespace VarIntCalculator.Services
{
    public enum AppTheme
    {
        Default,
        Dark,
        Light,
    }

    public sealed class AppSettings
    {
        public AppTheme Theme { get; set; } = AppTheme.Default;
        public VarintFormat Format { get; set; } = VarintFormat.Sqlite;
        public CalcDirection Direction { get; set; } = CalcDirection.Decode;
        public int PageSize { get; set; } = PageGeometry.DefaultPageSize;
        public int ReservedBytes { get; set; }
        public BTreeKind BTree { get; set; } = BTreeKind.TableLeaf;
        public int TutorialStep { get; set; }

        public PageGeometry Page
        {
            get
            {
                var page = new PageGeometry(PageSize, ReservedBytes, BTree);
                return page.IsValid ? page : PageGeometry.Default;
            }
        }
    }

    /// <summary>
    /// Settings and history as JSON in %APPDATA%\Elusive Data\Varint Calculator. A file that is
    /// missing or unreadable gives the defaults; writes go through a temporary file so a crash
    /// mid-write cannot leave half a file behind.
    /// </summary>
    public static class SettingsStore
    {
        public const int MaxHistory = 50;

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Folder { get; private set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elusive Data", "Varint Calculator");

        private static string SettingsPath => Path.Combine(Folder, "settings.json");

        private static string HistoryPath => Path.Combine(Folder, "history.json");

        public static AppSettings LoadSettings() => Read<AppSettings>(SettingsPath) ?? new AppSettings();

        public static void SaveSettings(AppSettings settings) => Write(SettingsPath, settings);

        public static List<HistoryEntry> LoadHistory()
        {
            var entries = Read<List<HistoryEntry>>(HistoryPath) ?? new List<HistoryEntry>();
            entries.RemoveAll(e => string.IsNullOrEmpty(e.Input));
            return entries.Take(MaxHistory).ToList();
        }

        public static void SaveHistory(IEnumerable<HistoryEntry> entries) => Write(HistoryPath, entries.Take(MaxHistory).ToList());

        private static T? Read<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                return null;
            }
        }

        private static void Write<T>(string path, T value)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Settings are a convenience: a locked or read-only profile must not stop the calculator.
            }
        }
    }
}
