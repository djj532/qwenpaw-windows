using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace QwenPaw
{
    public class NetworkConfig
    {
        public string Mode { get; set; } = "auto";
        public string LanHost { get; set; } = "10.73.1.100";
        public int LanPort { get; set; } = 49429;
        public string WanUrl { get; set; } = "https://paw.xdjj.asia/";
    }

    public partial class MainWindow : Window
    {
        private NetworkConfig _config = new NetworkConfig();
        private readonly string _configPath;
        private string _currentUrl = "";
        private bool _isInitialized = false;

        public MainWindow()
        {
            InitializeComponent();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "QwenPawClient");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "network.json");
            LoadConfig();
            Loaded += MainWindow_Loaded;
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize<NetworkConfig>(json) ?? new NetworkConfig();
                }
            }
            catch {}
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch {}
        }

        private async Task<bool> ProbeLanAsync(string host, int port)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var resp = await client.GetAsync($"http://{host}:{port}/api/auth/status");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> ResolveTargetUrlAsync()
        {
            if (_config.Mode == "lan")
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                statusText.Text = $"🏠 局域网直连 ({_config.LanHost}:{_config.LanPort})";
                return $"http://{_config.LanHost}:{_config.LanPort}/";
            }
            if (_config.Mode == "wan")
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                statusText.Text = "🌐 公网漫游 (paw.xdjj.asia)";
                return _config.WanUrl;
            }
            statusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            statusText.Text = "⚡ 正在智能探测最佳网络...";
            bool lanOk = await ProbeLanAsync(_config.LanHost, _config.LanPort);
            if (lanOk)
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                statusText.Text = $"⚡ 局域网极速 ({_config.LanHost}:{_config.LanPort})";
                return $"http://{_config.LanHost}:{_config.LanPort}/";
            }
            statusDot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            statusText.Text = "🌐 公网漫游 (paw.xdjj.asia)";
            return _config.WanUrl;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_config.Mode == "lan") modeSelector.SelectedIndex = 1;
            else if (_config.Mode == "wan") modeSelector.SelectedIndex = 2;
            else modeSelector.SelectedIndex = 0;
            _isInitialized = true;
            await InitWebViewAndNavigateAsync();
        }

        private async Task InitWebViewAndNavigateAsync()
        {
            progressBar.Visibility = Visibility.Visible;
            string target = await ResolveTargetUrlAsync();
            _currentUrl = target;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(appData, "QwenPawClient", "WebView2Data");
            var envOptions = new CoreWebView2EnvironmentOptions(
                "--disable-background-timer-throttling " +
                "--disable-backgrounding-occluded-windows " +
                "--disable-renderer-backgrounding"
            );
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
            await webView.EnsureCoreWebView2Async(env);
            webView.CoreWebView2.Settings.UserAgent += " QwenPawMobileApp/861129";
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.NavigationCompleted += (s, args) => { progressBar.Visibility = Visibility.Collapsed; };
            webView.CoreWebView2.Navigate(target);
        }

        private async void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            int idx = modeSelector.SelectedIndex;
            if (idx == 1) _config.Mode = "lan";
            else if (idx == 2) _config.Mode = "wan";
            else _config.Mode = "auto";
            SaveConfig();
            if (webView.CoreWebView2 != null)
            {
                progressBar.Visibility = Visibility.Visible;
                string target = await ResolveTargetUrlAsync();
                if (target != _currentUrl)
                {
                    _currentUrl = target;
                    webView.CoreWebView2.Navigate(target);
                }
                else { progressBar.Visibility = Visibility.Collapsed; }
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            string prompt = $"当前局域网地址为: {_config.LanHost}:{_config.LanPort}\n\n如需更改请输入新的 IP:端口 (例如 10.73.1.100:49429 或 192.168.1.10:49429):";
            string input = Microsoft.VisualBasic.Interaction.InputBox(prompt, "配置局域网直连地址", $"{_config.LanHost}:{_config.LanPort}");
            if (!string.IsNullOrWhiteSpace(input) && input.Contains(":"))
            {
                var parts = input.Split(":");
                if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    _config.LanHost = parts[0].Trim();
                    _config.LanPort = port;
                    SaveConfig();
                    MessageBox.Show($"局域网地址已更新为: {_config.LanHost}:{_config.LanPort}\n下次连接时将立即生效。", "更新成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
