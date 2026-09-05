using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace QwenPaw
{
    public partial class MainWindow : Window
    {
        private const string TargetUrl = "https://paw.xdjj.asia/";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Activated += MainWindow_Activated;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable all Chromium background throttling flags
                var browserArgs = "--disable-background-timer-throttling " +
                                  "--disable-backgrounding-occluded-windows " +
                                  "--disable-renderer-backgrounding " +
                                  "--disable-features=CalculateNativeWinOcclusion";

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QwenPawClient"
                );

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder,
                    options: new CoreWebView2EnvironmentOptions(browserArgs)
                );

                await webView.EnsureCoreWebView2Async(env);

                var settings = webView.CoreWebView2.Settings;
                settings.IsScriptEnabled = true;
                settings.IsWebMessageEnabled = true;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.IsStatusBarEnabled = false;

                // Append dedicated authentication fingerprint
                string defaultUa = settings.UserAgent;
                settings.UserAgent = defaultUa + " QwenPawMobileApp/861129";

                webView.NavigationCompleted += (s, args) =>
                {
                    progressBar.Visibility = Visibility.Collapsed;
                };

                webView.CoreWebView2.Navigate(TargetUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2 初始化失败: " + ex.Message + "\n请确保系统已安装 Microsoft Edge WebView2 Runtime。",
                    "QwenPaw 启动错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (webView?.CoreWebView2 != null)
            {
                // Seamlessly resync session if window was minimized or in background
                await webView.CoreWebView2.ExecuteScriptAsync(
                    "(function() {" +
                    "  window.dispatchEvent(new Event('focus'));" +
                    "  document.dispatchEvent(new Event('visibilitychange'));" +
                    "  window.dispatchEvent(new Event('online'));" +
                    "  var m = window.location.pathname.match(/\\/chat\\/([a-zA-Z0-9_-]+)/);" +
                    "  if (m && m[1]) {" +
                    "    window.dispatchEvent(new CustomEvent('qwenpaw:sidebar-select-session', { detail: { sessionId: m[1] } }));" +
                    "  }" +
                    "})();"
                );
            }
        }
    }
}
