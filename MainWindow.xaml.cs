using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace QwenPaw
{
    public class NetworkConfig
    {
        public string Mode { get; set; } = "auto";
        public string LanHost { get; set; } = "10.73.1.100";
        public int LanPort { get; set; } = 49429;
        public string WanUrl { get; set; } = "https://paw.xdjj.asia/";
        public string? LastChatId { get; set; } = null;
        public bool DisableLocalWebUI { get; set; } = false;
    }

    public partial class MainWindow : Window
    {
        private NetworkConfig _config = new NetworkConfig();
        private readonly string _configPath;
        private readonly string _legacyConfigPath;
        private string _currentUrl = "";
        private string _activeGatewayBaseUrl = "https://paw.xdjj.asia";
        private bool _isInitialized = false;

        private bool _recoveryDismissed = false;
        private DispatcherTimer? _recoverySafetyTimer;

        private const string GATEWAY_UA = "QwenPawMobileApp/861129";
        private const string AUTH_COOKIE = "qwenpaw_auth_session=2000000000:78d0377796268a579b447321d5ac58205f8ee3a426d4fd747c3a8aa713938f59; Path=/; Domain=paw.xdjj.asia; Secure; SameSite=None";

        private static readonly HttpClientHandler _proxyHandler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true
        };

        private static readonly HttpClient _proxyHttpClient = new HttpClient(_proxyHandler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public MainWindow()
        {
            InitializeComponent();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "QwenPawClient");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "config.json");
            _legacyConfigPath = Path.Combine(dir, "network.json");
            LoadConfig();
            Loaded += MainWindow_Loaded;
        }

        private void LoadConfig()
        {
            try
            {
                string targetFile = File.Exists(_configPath) ? _configPath : _legacyConfigPath;
                if (File.Exists(targetFile))
                {
                    var json = File.ReadAllText(targetFile);
                    _config = JsonSerializer.Deserialize<NetworkConfig>(json) ?? new NetworkConfig();
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
                // 同步写一份 network.json 保证向下兼容
                File.WriteAllText(_legacyConfigPath, json);
            }
            catch { }
        }

        private string? GetLocalWebUiDirectory()
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string candidate1 = Path.Combine(exeDir, "webui");
                if (Directory.Exists(candidate1) && File.Exists(Path.Combine(candidate1, "index.html")))
                {
                    return candidate1;
                }

                string candidate2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenPawClient", "webui");
                if (Directory.Exists(candidate2) && File.Exists(Path.Combine(candidate2, "index.html")))
                {
                    return candidate2;
                }
            }
            catch { }
            return null;
        }

        private async Task<bool> ProbeLanAsync(string host, int port)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(600) };
                var resp = await client.GetAsync($"http://{host}:{port}/api/auth/status");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> ResolveTargetGatewayAsync()
        {
            if (_config.Mode == "lan")
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                statusText.Text = $"🏠 局域网直连 ({_config.LanHost}:{_config.LanPort})";
                return $"http://{_config.LanHost}:{_config.LanPort}";
            }
            if (_config.Mode == "wan")
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                statusText.Text = "🌐 公网漫游 (paw.xdjj.asia)";
                return _config.WanUrl.TrimEnd('/');
            }

            statusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            statusText.Text = "⚡ 正在智能探测最佳网络...";
            bool lanOk = await ProbeLanAsync(_config.LanHost, _config.LanPort);
            if (lanOk)
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                statusText.Text = $"⚡ 局域网极速 ({_config.LanHost}:{_config.LanPort})";
                return $"http://{_config.LanHost}:{_config.LanPort}";
            }

            statusDot.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            statusText.Text = "🌐 公网漫游 (paw.xdjj.asia)";
            return _config.WanUrl.TrimEnd('/');
        }

        private string BuildStartupUrl(bool useLocalWebUi)
        {
            string baseTarget = useLocalWebUi ? "https://appassets.local/index.html" : _activeGatewayBaseUrl;
            if (!string.IsNullOrWhiteSpace(_config.LastChatId))
            {
                string chatId = _config.LastChatId.Trim();
                if (useLocalWebUi)
                {
                    baseTarget += $"#/chat/{chatId}";
                }
                else
                {
                    baseTarget = baseTarget.TrimEnd('/') + $"/#/chat/{chatId}";
                }
            }
            return baseTarget;
        }

        private void SetupRecoveryView()
        {
            _recoveryDismissed = false;
            recoveryOverlay.Opacity = 1.0;
            recoveryOverlay.Visibility = Visibility.Visible;
            errorOverlay.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(_config.LastChatId))
            {
                recoveryTitle.Text = "QwenPaw";
                recoveryStatus.Text = "恢复对话中…";
            }
            else
            {
                recoveryTitle.Text = "QwenPaw";
                recoveryStatus.Text = "准备就绪中…";
            }

            // 设立安全保底计时器（3.5 秒后自动淡出），防止页面异常时遮挡操作
            _recoverySafetyTimer?.Stop();
            _recoverySafetyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(3500)
            };
            _recoverySafetyTimer.Tick += (s, e) =>
            {
                _recoverySafetyTimer.Stop();
                DismissRecoveryOverlay();
            };
            _recoverySafetyTimer.Start();
        }

        private void DismissRecoveryOverlay()
        {
            if (_recoveryDismissed) return;
            _recoveryDismissed = true;
            _recoverySafetyTimer?.Stop();

            if (recoveryOverlay.Visibility != Visibility.Visible) return;

            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (s, e) =>
            {
                recoveryOverlay.Visibility = Visibility.Collapsed;
                recoveryOverlay.Opacity = 1.0;
            };
            recoveryOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void UpdateLastChatId(string? urlOrHash)
        {
            if (string.IsNullOrWhiteSpace(urlOrHash)) return;
            var match = Regex.Match(urlOrHash, @"(?:#/|/)chat/([a-zA-Z0-9_-]+)");
            if (match.Success)
            {
                string id = match.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(id) && id != _config.LastChatId)
                {
                    _config.LastChatId = id;
                    SaveConfig();
                }
            }
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
            SetupRecoveryView();

            _activeGatewayBaseUrl = await ResolveTargetGatewayAsync();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userDataFolder = Path.Combine(appData, "QwenPawClient", "WebView2Data");

            var envOptions = new CoreWebView2EnvironmentOptions(
                "--disable-background-timer-throttling " +
                "--disable-backgrounding-occluded-windows " +
                "--disable-renderer-backgrounding"
            );

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.Settings.UserAgent += $" {GATEWAY_UA}";
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // 1. 本地 WebUI 虚拟域名映射支持
            string? localWebUiDir = GetLocalWebUiDirectory();
            bool canUseLocal = !_config.DisableLocalWebUI && !string.IsNullOrEmpty(localWebUiDir);

            if (canUseLocal)
            {
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "appassets.local",
                    localWebUiDir!,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // 2. API 代理过滤器（拦截 /api/*, /console/*, /workspace/* 并转发远端网关）
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://appassets.local/api/*", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://appassets.local/console/*", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.AddWebResourceRequestedFilter("https://appassets.local/workspace/*", CoreWebView2WebResourceContext.All);
                webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
            }

            // 3. 注入路由状态监听脚本，捕获前端 SPA 会话变更
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                (function() {
                    function reportRoute() {
                        try {
                            if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                                window.chrome.webview.postMessage(JSON.stringify({
                                    type: 'route_change',
                                    hash: window.location.hash,
                                    url: window.location.href
                                }));
                            }
                        } catch(e) {}
                    }
                    window.addEventListener('hashchange', reportRoute);
                    window.addEventListener('popstate', reportRoute);
                    var origPushState = history.pushState;
                    history.pushState = function() {
                        origPushState.apply(this, arguments);
                        reportRoute();
                    };
                    var origReplaceState = history.replaceState;
                    history.replaceState = function() {
                        origReplaceState.apply(this, arguments);
                        reportRoute();
                    };
                    reportRoute();
                })();
            ");

            webView.CoreWebView2.WebMessageReceived += (s, args) =>
            {
                try
                {
                    string raw = args.TryGetWebMessageAsString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        using var doc = JsonDocument.Parse(raw);
                        if (doc.RootElement.TryGetProperty("hash", out var hashEl))
                        {
                            UpdateLastChatId(hashEl.GetString());
                        }
                        else if (doc.RootElement.TryGetProperty("url", out var urlEl))
                        {
                            UpdateLastChatId(urlEl.GetString());
                        }
                    }
                }
                catch { }
            };

            // 4. 页面导航完成事件（淡出恢复视图、降级处理）
            webView.NavigationCompleted += async (s, args) =>
            {
                progressBar.Visibility = Visibility.Collapsed;

                if (args.IsSuccess)
                {
                    DismissRecoveryOverlay();
                    errorOverlay.Visibility = Visibility.Collapsed;

                    try
                    {
                        var hashRaw = await webView.ExecuteScriptAsync("location.hash");
                        if (!string.IsNullOrEmpty(hashRaw) && hashRaw != "null")
                        {
                            UpdateLastChatId(hashRaw.Trim('"'));
                        }
                    }
                    catch { }
                    return;
                }

                if (args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    return;
                }

                // 虚拟域名加载失败降级
                if (_currentUrl.StartsWith("https://appassets.local", StringComparison.OrdinalIgnoreCase))
                {
                    statusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    statusText.Text = "⚠️ 本地静态包异常，已自动切入云端远程...";
                    _config.DisableLocalWebUI = true;
                    SaveConfig();

                    string fallbackUrl = BuildStartupUrl(useLocalWebUi: false);
                    _currentUrl = fallbackUrl;
                    webView.CoreWebView2.Navigate(fallbackUrl);
                    return;
                }

                // 局域网直连超时降级
                if (_currentUrl.Contains(_config.LanHost) && _currentUrl.Contains(_config.LanPort.ToString()))
                {
                    statusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    statusText.Text = "⚠️ 局域网连接超时，已自动无感切入公网漫游...";
                    _activeGatewayBaseUrl = _config.WanUrl.TrimEnd('/');
                    string wanTarget = BuildStartupUrl(useLocalWebUi: false);
                    _currentUrl = wanTarget;
                    webView.CoreWebView2.Navigate(wanTarget);
                    return;
                }

                // 彻底失败时展示兜底重试视图
                DismissRecoveryOverlay();
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                statusText.Text = "❌ 连接异常，请点击【🔄 刷新】重试";
                errorDetailText.Text = $"未能成功连接至服务：{_currentUrl}\n状态: {args.WebErrorStatus}";
                errorOverlay.Visibility = Visibility.Visible;
            };

            // 5. 执行首次加载
            string target = BuildStartupUrl(canUseLocal);
            _currentUrl = target;
            webView.CoreWebView2.Navigate(target);
        }

        private async void CoreWebView2_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var deferral = e.GetDeferral();
            try
            {
                var uri = new Uri(e.Request.Uri);
                if (!uri.Host.Equals("appassets.local", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string path = uri.AbsolutePath;
                if (!path.StartsWith("/api/") && !path.StartsWith("/console/") && !path.StartsWith("/workspace/"))
                {
                    return;
                }

                string remoteBase = _activeGatewayBaseUrl.TrimEnd('/');
                string targetUrl = remoteBase + uri.PathAndQuery;

                using var proxyRequest = new HttpRequestMessage(new HttpMethod(e.Request.Method), targetUrl);

                // 复制请求体
                if (e.Request.Content != null &&
                    !string.Equals(e.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(e.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    var reqStream = new MemoryStream();
                    await e.Request.Content.CopyToAsync(reqStream);
                    reqStream.Position = 0;
                    proxyRequest.Content = new StreamContent(reqStream);
                }

                // 复制请求头（过滤 host, origin, content-length, content-type）
                string? contentType = null;
                foreach (var header in e.Request.Headers)
                {
                    string key = header.Key;
                    string val = header.Value;

                    if (key.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("origin", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("content-length", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                    {
                        contentType = val;
                        continue;
                    }

                    if (!proxyRequest.Headers.TryAddWithoutValidation(key, val) && proxyRequest.Content != null)
                    {
                        proxyRequest.Content.Headers.TryAddWithoutValidation(key, val);
                    }
                }

                if (contentType != null && proxyRequest.Content != null)
                {
                    proxyRequest.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }

                // 注入网关鉴权 UA
                proxyRequest.Headers.Remove("User-Agent");
                proxyRequest.Headers.TryAddWithoutValidation("User-Agent", $"{GATEWAY_UA} QwenPawWindowsApp/1.2.0");

                // 注入鉴权 Cookie
                string cookieVal = AUTH_COOKIE;
                foreach (var header in e.Request.Headers)
                {
                    if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(header.Value) && !header.Value.Contains("qwenpaw_auth_session"))
                        {
                            cookieVal = $"{header.Value}; {AUTH_COOKIE}";
                        }
                        break;
                    }
                }

                proxyRequest.Headers.Remove("Cookie");
                proxyRequest.Headers.TryAddWithoutValidation("Cookie", cookieVal);
                proxyRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

                var respMsg = await _proxyHttpClient.SendAsync(proxyRequest);

                var headerSb = new System.Text.StringBuilder();
                foreach (var h in respMsg.Headers)
                {
                    headerSb.Append($"{h.Key}: {string.Join(", ", h.Value)}\r\n");
                }
                foreach (var h in respMsg.Content.Headers)
                {
                    headerSb.Append($"{h.Key}: {string.Join(", ", h.Value)}\r\n");
                }

                var respBytes = await respMsg.Content.ReadAsByteArrayAsync();
                var respStream = new MemoryStream(respBytes);

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            respStream,
                            (int)respMsg.StatusCode,
                            string.IsNullOrEmpty(respMsg.ReasonPhrase) ? "OK" : respMsg.ReasonPhrase,
                            headerSb.ToString()
                        );
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"detail\":\"Gateway proxy error: {ex.Message}\"}}");
                        var mem = new MemoryStream(bytes);
                        e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            mem,
                            502,
                            "Bad Gateway",
                            "Content-Type: application/json; charset=utf-8\r\n"
                        );
                    }
                    catch { }
                });
            }
            finally
            {
                deferral.Complete();
            }
        }

        public async Task NavigateToChatAsync(string chatId)
        {
            if (webView?.CoreWebView2 == null) return;
            string targetHash = string.IsNullOrWhiteSpace(chatId) ? "#/" : $"#/chat/{chatId.Trim()}";
            string js = $@"(function() {{
                try {{
                    var target = '{targetHash}';
                    if (window.location.hash !== target) {{
                        window.location.hash = target;
                    }}
                }} catch(e) {{}}
            }})();";
            await webView.ExecuteScriptAsync(js);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await PerformHardRefreshAsync();
        }

        private async void BtnErrorSwitchMode_Click(object sender, RoutedEventArgs e)
        {
            _config.Mode = "wan";
            modeSelector.SelectedIndex = 2;
            SaveConfig();
            await PerformHardRefreshAsync();
        }

        private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F5)
            {
                e.Handled = true;
                await PerformHardRefreshAsync();
            }
        }

        private async Task PerformHardRefreshAsync()
        {
            if (!_isInitialized || webView?.CoreWebView2 == null) return;
            progressBar.Visibility = Visibility.Visible;
            errorOverlay.Visibility = Visibility.Collapsed;
            SetupRecoveryView();

            statusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            statusText.Text = "⚡ 正在重新探测最佳网络并强刷...";

            _activeGatewayBaseUrl = await ResolveTargetGatewayAsync();

            string? localWebUiDir = GetLocalWebUiDirectory();
            bool canUseLocal = !_config.DisableLocalWebUI && !string.IsNullOrEmpty(localWebUiDir);

            string target = BuildStartupUrl(canUseLocal);
            _currentUrl = target;
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
                _activeGatewayBaseUrl = await ResolveTargetGatewayAsync();

                string? localWebUiDir = GetLocalWebUiDirectory();
                bool canUseLocal = !_config.DisableLocalWebUI && !string.IsNullOrEmpty(localWebUiDir);

                string target = BuildStartupUrl(canUseLocal);
                if (target != _currentUrl)
                {
                    _currentUrl = target;
                    webView.CoreWebView2.Navigate(target);
                }
                else
                {
                    progressBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            string prompt = $"当前局域网地址为: {_config.LanHost}:{_config.LanPort}\n\n如需更改请输入新的 IP:端口 (例如 10.73.1.100:49429 或 192.168.1.10:49429):";
            string input = Microsoft.VisualBasic.Interaction.InputBox(prompt, "配置局域网直连地址", $"{_config.LanHost}:{_config.LanPort}");
            if (!string.IsNullOrWhiteSpace(input) && input.Contains(":"))
            {
                var parts = input.Split(':');
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
