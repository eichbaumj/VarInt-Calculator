using System.Reflection;
using System.Windows;
using VarIntCalculator.Services;

namespace VarIntCalculator
{
    public partial class App : Application
    {
        /// <summary>Index of the theme dictionary in App.xaml's merged dictionaries.</summary>
        private const int ThemeSlot = 1;

        public static AppSettings Settings { get; private set; } = new();

        /// <summary>
        /// The theme the host application chose, when the calculator was opened by Firefly with
        /// <c>--theme Default|Dark|Light</c>; null when it runs on its own. A host's theme is shown
        /// but never saved, so the standalone calculator keeps its own choice.
        /// </summary>
        public static AppTheme? HostTheme { get; private set; }

        /// <summary>The theme on screen: the host's when there is one, otherwise the saved one.</summary>
        public static AppTheme ActiveTheme => HostTheme ?? Settings.Theme;

        public static string Version { get; } =
            Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

        protected override void OnStartup(StartupEventArgs e)
        {
            DisableTextServices();
            base.OnStartup(e);

            Settings = SettingsStore.LoadSettings();
            HostTheme = HostThemeFrom(e.Args);
            ApplyTheme(ActiveTheme);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>Reads <c>--theme &lt;name&gt;</c> from the command line; anything else is ignored.</summary>
        internal static AppTheme? HostThemeFrom(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], "--theme", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse(args[i + 1], ignoreCase: true, out AppTheme theme) && Enum.IsDefined(theme))
                    return theme;
            }
            return null;
        }

        public static void ApplyTheme(AppTheme theme)
        {
            var dictionary = (ResourceDictionary)LoadComponent(
                new Uri($"/VarIntCalculator;component/Themes/{theme}.xaml", UriKind.Relative));
            Current.Resources.MergedDictionaries[ThemeSlot] = dictionary;
        }

        /// <summary>
        /// Treats the Text Services Framework as not installed, as the Firefly analyzers do: an
        /// intermittent OS-side DivideByZero in TSF's keystroke manager otherwise takes a WPF app
        /// down on ordinary typing. The calculator reads hex and digits, so IME composition is not
        /// needed. If WPF's internals change, this does nothing.
        /// </summary>
        private static void DisableTextServices()
        {
            try
            {
                var loader = typeof(DependencyObject).Assembly.GetType("MS.Internal.TextServicesLoader");
                var field = loader?.GetField("s_servicesInstalled", BindingFlags.Static | BindingFlags.NonPublic);
                if (field is not null)
                    field.SetValue(null, Enum.ToObject(field.FieldType, 2));   // InstallState.NotInstalled
            }
            catch (Exception ex) when (ex is ArgumentException or FieldAccessException or TargetException)
            {
            }
        }
    }
}
