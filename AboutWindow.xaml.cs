using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace VarIntCalculator
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "Version " + App.Version;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Close();

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No browser or mail client registered: the address is on screen to copy by hand.
            }
            e.Handled = true;
        }
    }
}
