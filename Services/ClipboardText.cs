using System.Runtime.InteropServices;
using System.Windows;

namespace VarIntCalculator.Services
{
    /// <summary>
    /// Clipboard access that survives another program holding the clipboard for a moment
    /// (CLIPBRD_E_CANT_OPEN): a few short retries, then a plain failure the caller reports.
    /// </summary>
    public static class ClipboardText
    {
        private const int Attempts = 5;

        public static bool TrySet(string text)
        {
            for (int i = 0; i < Attempts; i++)
            {
                try
                {
                    Clipboard.SetDataObject(text, copy: true);
                    return true;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(40);
                }
            }
            return false;
        }

        public static string? TryGet()
        {
            for (int i = 0; i < Attempts; i++)
            {
                try
                {
                    return Clipboard.ContainsText() ? Clipboard.GetText() : null;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(40);
                }
            }
            return null;
        }
    }
}
