using WinUI3Twikit.Controls;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;

namespace WinUI3Twikit
{
    public sealed partial class MainWindow : Window
    {
        private DispatcherTimer? _clockTimer;
        private PointerEventHandler? _contentPointerPressedHandler;
        private bool _composeDialogOpen;

        private void CustomizeWindow()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            // タイトルバーのアイコン
            var iconPath = Path.Combine(AppContext.BaseDirectory, "TwitterIcon48.ico");
            appWindow.SetIcon(iconPath);

            // Presenter を取得（標準の OverlappedPresenter）
            OverlappedPresenter? presenter = appWindow.Presenter as OverlappedPresenter;

            // タイトルバーの色はテーマに合わせる（ライト/ダーク切り替え時にも追従する）
            ApplyTitleBarTheme(appWindow);
            if (Content is FrameworkElement root)
            {
                root.ActualThemeChanged += (_, _) => ApplyTitleBarTheme(appWindow);
            }

            // 必要なら最大化/最小化ボタンの制御
            if (presenter is not null)
            {
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
            }
        }

        private void ApplyTitleBarTheme(AppWindow appWindow)
        {
            bool isLight = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Light;

            // ダークは従来どおり #202020、ライトは Mica のライト基調に近い #F3F3F3
            var background = isLight
                ? Color.FromArgb(255, 243, 243, 243)
                : Color.FromArgb(255, 32, 32, 32);
            var buttonForeground = isLight ? Colors.Black : Colors.White;

            var titleBar = appWindow.TitleBar;

            // タイトルバー背景
            titleBar.BackgroundColor = background;
            titleBar.InactiveBackgroundColor = background;

            // ボタン背景
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonInactiveBackgroundColor = background;

            // ボタン前景（アイコン）
            titleBar.ButtonForegroundColor = buttonForeground;
            titleBar.ButtonInactiveForegroundColor = Colors.Gray;
        }

        public void SetWindowTitle(string pageName)
        {
            string appName = "WinUI 3 Twikit";  // ← アプリ名をここで統一管理
            string title = $"{pageName} / {appName}";

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Title = title;
        }


        public void ShowLoading(bool isLoading)
        {
            if (App.IsShuttingDown || LoadingPanel == null)
            {
                return;
            }

            LoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        // ナビゲーション後にページがロードされたら初期状態をオフに
        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            ShowLoading(false);
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            if (GetPageNavInfo(e.SourcePageType) is { } info)
            {
                SelectNavItemByTag(info.Tag);
                SetWindowTitle(info.Title);
            }
        }

        public MainWindow()
        {
            this.InitializeComponent();
            CustomizeWindow();
            InitializeTrayIcon();

            // NavView初期化...
            this.ContentFrame.Navigated += ContentFrame_Navigated;
            AddBackNavigators();
            AddComposeAccelerator();
            NavView.SelectedItem = NavView.MenuItems[0];
            this.ContentFrame.Navigate(typeof(TweetPage));

            this.Closed += MainWindow_Closed;

            App.ViewModels.Timeline.PropertyChanged += Timeline_PropertyChanged;
            UpdateTweetCountDisplay();

            _ = InitializeServerAndClock();
        }

        private void Timeline_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimelineViewModel.ForYouTweetCount)
                or nameof(TimelineViewModel.LatestTweetCount))
            {
                UpdateTweetCountDisplay();
            }
        }

        private void UpdateTweetCountDisplay()
        {
            if (App.IsShuttingDown)
            {
                return;
            }

            var timeline = App.ViewModels.Timeline;
            if (ForYouTweetCountText != null)
            {
                ForYouTweetCountText.Text = $"おすすめ: {timeline.ForYouTweetCount}";
            }
            if (LatestTweetCountText != null)
            {
                LatestTweetCountText.Text = $"最新: {timeline.LatestTweetCount}";
            }
        }

        private async System.Threading.Tasks.Task InitializeServerAndClock()
        {
            await ServerManager.StartServerAsync(ServerStatusText);

            if (App.IsShuttingDown)
            {
                return;
            }

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();
        }

        /// <summary>
        /// 設定ページなどから API サーバーを再起動する（cookie 反映用）。
        /// </summary>
        public System.Threading.Tasks.Task RestartServerAsync()
        {
            return ServerManager.RestartServerAsync(ServerStatusText);
        }

        private void ClockTimer_Tick(object? sender, object e)
        {
            if (App.IsShuttingDown || ClockText == null)
            {
                return;
            }

            ClockText.Text = DateTime.Now.ToString("HH:mm:ss") + "\n" + DateTime.Now.ToString("yyyy/MM/dd");
        }

        private void InitializeTrayIcon()
        {
            var openCommand = new XamlUICommand { Label = "開く" };
            openCommand.ExecuteRequested += TrayOpenCommand_ExecuteRequested;

            var exitCommand = new XamlUICommand { Label = "終了" };
            exitCommand.ExecuteRequested += TrayExitCommand_ExecuteRequested;

            TrayIcon.ContextFlyout = new MenuFlyout
            {
                Items =
                {
                    new MenuFlyoutItem { Command = openCommand },
                    new MenuFlyoutItem { Command = exitCommand },
                },
            };

            TrayIcon.ForceCreate();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (App.HandleClosedEvents && !App.IsShuttingDown)
            {
                args.Handled = true;
                this.Hide();
                return;
            }

            _clockTimer?.Stop();
            _clockTimer = null;

            App.ViewModels.Timeline.PropertyChanged -= Timeline_PropertyChanged;
            this.ContentFrame.Navigated -= ContentFrame_Navigated;
            if (_contentPointerPressedHandler is not null && Content is UIElement root)
            {
                root.RemoveHandler(UIElement.PointerPressedEvent, _contentPointerPressedHandler);
                _contentPointerPressedHandler = null;
            }
            TrayIcon?.Dispose();
            App.NotifyMainWindowClosing();
            ServerManager.StopServer();
        }

        private void TrayOpenCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                this.Show();
                this.Activate();
            });
        }

        private void TrayExitCommand_ExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                App.RequestShutdown();
                TrayIcon?.Dispose();
                this.Close();
            });
        }

        /// <summary>
        /// ツイートカードのプロフィール画像などからプロフィール検索へ遷移し、
        /// 指定ユーザーを検索して表示する（既存結果は SearchAsync 内で破棄される）。
        /// </summary>
        public void NavigateToProfileSearch(string? screenName)
        {
            var normalized = ProfileSearchViewModel.NormalizeScreenName(screenName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var query = "@" + normalized;

            // SearchAsync は最初の await 前に LastQuery をセットする。
            // ナビ前に開始し、OnNavigatedTo / Loaded で SearchBox に反映できるようにする。
            // ページ側では再検索しない（二重ロード防止）。
            var searchTask = App.ViewModels.ProfileSearch.SearchAsync(query);

            NavigateToPage(typeof(ProfileSearchPage));

            _ = FinishProfileSearchLoadingAsync(searchTask);
        }

        private async System.Threading.Tasks.Task FinishProfileSearchLoadingAsync(
            System.Threading.Tasks.Task searchTask)
        {
            ShowLoading(true);
            try
            {
                await searchTask;
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private void SelectNavItemByTag(string tag)
        {
            foreach (var obj in NavView.MenuItems)
            {
                if (obj is NavigationViewItem item &&
                    string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
                {
                    NavView.SelectedItem = item;
                    return;
                }
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToPage(typeof(SettingsPage));
                return;
            }

            var item = args.InvokedItemContainer as NavigationViewItem;
            var pageType = GetPageType(item?.Tag as string);
            if (pageType is not null)
            {
                NavigateToPage(pageType);
            }
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            TryGoBack();
        }

        private void AddBackNavigators()
        {
            var altLeft = new KeyboardAccelerator
            {
                Key = VirtualKey.Left,
                Modifiers = VirtualKeyModifiers.Menu,
            };
            altLeft.Invoked += BackAccelerator_Invoked;
            NavView.KeyboardAccelerators.Add(altLeft);

            var goBack = new KeyboardAccelerator { Key = VirtualKey.GoBack };
            goBack.Invoked += BackAccelerator_Invoked;
            NavView.KeyboardAccelerators.Add(goBack);

            if (Content is UIElement root)
            {
                _contentPointerPressedHandler = ContentRoot_PointerPressed;
                root.AddHandler(
                    UIElement.PointerPressedEvent,
                    _contentPointerPressedHandler,
                    handledEventsToo: true);
            }
        }

        private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = TryGoBack();
        }

        private void AddComposeAccelerator()
        {
            if (Content is not UIElement root)
            {
                return;
            }

            // Letter keys must stay available to text boxes. PreviewKeyDown sees the key
            // first and leaves it unhandled while a text control has focus.
            root.PreviewKeyDown += ComposeRoot_PreviewKeyDown;
        }

        private void ComposeRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.N || HasComposeBlockedModifier())
            {
                return;
            }

            var xamlRoot = (sender as FrameworkElement)?.XamlRoot;
            if (IsTextEntryFocused(xamlRoot))
            {
                return;
            }

            e.Handled = true;
            if (_composeDialogOpen)
            {
                return;
            }

            _ = ShowComposeDialogAsync();
        }

        private static bool HasComposeBlockedModifier()
        {
            return IsKeyDown(VirtualKey.Control)
                || IsKeyDown(VirtualKey.Shift)
                || IsKeyDown(VirtualKey.Menu);
        }

        private static bool IsKeyDown(VirtualKey key)
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
            return state.HasFlag(CoreVirtualKeyStates.Down);
        }

        private static bool IsTextEntryFocused(XamlRoot? xamlRoot)
        {
            if (xamlRoot is null)
            {
                return false;
            }

            DependencyObject? current = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
            while (current is not null)
            {
                if (current is TextBox or RichEditBox or PasswordBox or NumberBox or AutoSuggestBox)
                {
                    return true;
                }

                if (current is ComboBox { IsEditable: true })
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private async Task ShowComposeDialogAsync()
        {
            if (_composeDialogOpen)
            {
                return;
            }

            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return;
            }

            _composeDialogOpen = true;
            try
            {
                var composer = new ComposeTweetControl();
                var scroll = new ScrollViewer
                {
                    Content = composer,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = Math.Max(240, root.XamlRoot.Size.Height - 160),
                };

                var dialog = new ContentDialog
                {
                    Title = "ツイート",
                    Content = scroll,
                    CloseButtonText = "閉じる",
                    DefaultButton = ContentDialogButton.None,
                    XamlRoot = root.XamlRoot,
                    RequestedTheme = root.ActualTheme,
                };
                var dialogWidth = Math.Min(680d * 1.5, Math.Max(320d, root.XamlRoot.Size.Width - 48));
                dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
                dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
                dialog.Resources["ContentDialogMaxHeight"] = Math.Max(320, root.XamlRoot.Size.Height - 48);

                dialog.Opened += (_, _) => composer.FocusInput();
                composer.TweetPosted += (_, _) =>
                {
                    try
                    {
                        dialog.Hide();
                    }
                    catch (Exception hideEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Compose dialog hide failed: {hideEx.Message}");
                    }
                };
                dialog.Closing += (_, args) =>
                {
                    if (composer.IsPosting)
                    {
                        args.Cancel = true;
                    }
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Compose dialog failed: {ex.Message}");
            }
            finally
            {
                _composeDialogOpen = false;
            }
        }

        private void ContentRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(sender as UIElement).Properties.IsXButton1Pressed)
            {
                e.Handled = TryGoBack();
            }
        }

        private void NavigateToPage(Type pageType)
        {
            if (ContentFrame.CurrentSourcePageType == pageType)
            {
                return;
            }

            ContentFrame.Navigate(pageType);
        }

        private bool TryGoBack()
        {
            if (!ContentFrame.CanGoBack)
            {
                return false;
            }

            ContentFrame.GoBack();
            return true;
        }

        private static Type? GetPageType(string? tag) => tag switch
        {
            "home" => typeof(TweetPage),
            "timeline" => typeof(TimelinePage),
            "search" => typeof(SearchPage),
            "notifications" => typeof(NotificationsPage),
            "lists" => typeof(ListsPage),
            "profilesearch" => typeof(ProfileSearchPage),
            "myprofile" => typeof(MyProfilePage),
            "settings" => typeof(SettingsPage),
            _ => null,
        };

        private static (string Tag, string Title)? GetPageNavInfo(Type? pageType)
        {
            if (pageType == typeof(TweetPage)) return ("home", "ツイート");
            if (pageType == typeof(TimelinePage)) return ("timeline", "ホーム");
            if (pageType == typeof(SearchPage)) return ("search", "検索");
            if (pageType == typeof(NotificationsPage)) return ("notifications", "通知");
            if (pageType == typeof(ListsPage)) return ("lists", "リスト");
            if (pageType == typeof(ProfileSearchPage)) return ("profilesearch", "プロフィール検索");
            if (pageType == typeof(MyProfilePage)) return ("myprofile", "プロフィール");
            if (pageType == typeof(SettingsPage)) return ("settings", "設定");
            return null;
        }

    }
}
