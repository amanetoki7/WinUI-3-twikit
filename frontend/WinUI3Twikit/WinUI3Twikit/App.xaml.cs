// App.xaml.cs
using Microsoft.UI.Xaml;

namespace WinUI3Twikit
{
    public partial class App : Application
    {
        public static MainWindow? MainWindow { get; private set; }   // MainWindow型に変更
        public static AppViewModels ViewModels { get; } = new();
        public static bool IsShuttingDown { get; private set; }
        public static bool HandleClosedEvents { get; set; } = true;

        public static void NotifyMainWindowClosing()
        {
            IsShuttingDown = true;
            MainWindow = null;
        }

        public static void RequestShutdown()
        {
            IsShuttingDown = true;
            HandleClosedEvents = false;
        }

        public App()
        {
            InitializeComponent();

            // UI イベントハンドラー内で起きた例外でプロセスごと落ちないようにする（ログに残して継続）。
            UnhandledException += OnUnhandledException;
        }

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");
            e.Handled = true;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Activate();
        }
    }
}
