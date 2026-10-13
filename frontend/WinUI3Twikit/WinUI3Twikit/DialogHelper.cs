using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    /// <summary>
    /// ContentDialog は同じ XamlRoot で同時に 1 つしか開けない（2 つ目は例外になる）。
    /// ここを通して開き、開いている間は 2 つ目を無視する（WinUI-3-twikit-deck より移植）。
    /// </summary>
    internal static class DialogHelper
    {
        private static int _open;

        public static bool IsDialogOpen => Volatile.Read(ref _open) != 0;

        public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
        {
            if (Interlocked.CompareExchange(ref _open, 1, 0) != 0)
            {
                return ContentDialogResult.None;
            }

            try
            {
                return await dialog.ShowAsync();
            }
            finally
            {
                Volatile.Write(ref _open, 0);
            }
        }
    }
}
