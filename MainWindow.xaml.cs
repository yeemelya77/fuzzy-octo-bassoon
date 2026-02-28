using NAudio.Wave;
using PlayNow.Client.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using System.Security.Principal;
// ВАЖНО: Эти два псевдонима решают проблему ошибок (CS0104)
using WinForms = System.Windows.Forms;

namespace PlayNow.Client
{
    public partial class MainWindow : Window
    {
        private readonly VpnManager _vpnManager;
        private bool _isMiniMode = false;
        private readonly GithubService _githubService;
        private string _selectedServer = "Auto";
        private Dictionary<string, string> _serverData = new Dictionary<string, string>();
        private Dictionary<string, AppConfig> _remoteAppsDb = new Dictionary<string, AppConfig>();
        private List<string> _selectedApps = new List<string>();
        // БАЗА ДАННЫХ ДЛЯ ПРОВЕРКИ (ID игры -> Адрес сервера)
        private readonly Dictionary<string, string> _appPingMap = new Dictionary<string, string>
        {
            // Шутеры
            {"cs2", "api.steampowered.com"}, // Steam API (Valve)
            {"valorant", "auth.riotgames.com"}, // Riot Auth
            {"pubg", "prod-live-entry.playbattlegrounds.com"}, // PUBG Login
            {"apex", "r2-pc.r5prod.stryder.respawn.com"}, // Apex Auth
            {"cod", "demonware.net"}, // COD Services
            {"fortnite", "account-public-service-prod.ol.epicgames.com"}, // Epic Auth
            {"siege", "connect.ubi.com"}, // Ubisoft
            {"overwatch", "battle.net"}, // Bnet
            {"warface", "warface.com"},
            {"the_finals", "discovery.embark.games"},
            
            // Выживание / Реализм
            {"tarkov", "prod.escapefromtarkov.com"}, // Tarkov Backend
            {"rust", "api.facepunch.com"},
            {"dayz", "dayz.com"},
            {"dbd", "latest.dbd.bhvr.com"}, // Dead by Daylight
            
            // MOBA
            {"dota2", "api.steampowered.com"},
            {"league", "auth.riotgames.com"},
            
            // Лаунчеры
            {"steam", "store.steampowered.com"},
            {"epic", "store.epicgames.com"},
            {"battlenet", "battle.net"},
            {"discord", "gateway.discord.gg"},
            {"telegram", "api.telegram.org"},
            
            // Браузеры (проверяем популярные сайты)
            {"chrome", "google.com"},
            {"firefox", "mozilla.org"},
            {"yandex_browser", "yandex.ru"},
            
            // Остальное (добавь по аналогии, если чего-то нет, будет проверка google.com)
            {"genshin", "sdk-os-static.hoyoverse.com"},
            {"roblox", "roblox.com"},
            {"twitch", "twitch.tv"},
            {"youtube", "youtube.com"}
        };

        // Состояние
        private bool _showLoadMode = false;
        private string _licenseKey = "";
        private string? _updateUrl = null; // Храним ссылку на обнову
        private readonly string _licenseFilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory, "license.key");

        private System.Threading.Timer? _pingTimer;
        private System.Threading.Timer? _heartbeatTimer;
        private readonly List<int> _pingHistory = new List<int>(new int[60]);
        private WaveInEvent? _waveIn;
        private Dictionary<string, string> _pingCache = new Dictionary<string, string>();

        // ТРЕЙ (Используем псевдоним WinForms, чтобы не конфликтовало)
        private WinForms.NotifyIcon? _trayIcon;
        private bool _isRealExit = false;

        // --- КОНСТРУКТОР ---
        public MainWindow()
        {
            // 1. ПРОВЕРКА АДМИНА
            if (!IsAdministrator())
            {
                MessageBox.Show("Для работы VPN требуются права Администратора!\nПожалуйста, запустите программу от имени Администратора.",
                                "Ошибка прав доступа", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            InitializeComponent();
            InitializeTray();

            _vpnManager = new VpnManager();
            _githubService = new GithubService();

            _ = LoadRealServersAsync();
            CheckLicenseOnStartup();

            // Запускаем тихую проверку обновлений
            _ = CheckUpdatesSilentAsync();

            _ = CheckNewsOnStartup();
            this.Loaded += (s, e) => Services.WindowEffect.EnableBlur(this);
        }

        // --- ЛОГИКА ТРЕЯ ---
        private void InitializeTray()
        {
            _trayIcon = new WinForms.NotifyIcon();
            try
            {
                // Берем иконку из самого EXE
                string exePath = Environment.ProcessPath!;
                _trayIcon.Icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
            }
            catch
            {
                // Если не вышло, стандартная
                _trayIcon.Icon = Drawing.SystemIcons.Application;
            }

            _trayIcon.Text = "PlayNow Client";
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) => ShowWindow();

            // Контекстное меню трея
            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("📂 Открыть", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("❌ Выход", null, (s, e) => RealExit());
            _trayIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void RealExit()
        {
            _isRealExit = true; // Разрешаем закрытие

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            if (_vpnManager != null && _vpnManager.IsConnected) _vpnManager.Disconnect();
            _waveIn?.StopRecording();
            _waveIn?.Dispose();

            // Явно указываем Application из WPF
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }

        // Перехват закрытия окна
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true; // Отменяем закрытие
                this.Hide();     // Прячем

                // Показываем балун (уведомление)
                _trayIcon?.ShowBalloonTip(3000, "PlayNow", "Свернуто в трей. Туннель работает.", WinForms.ToolTipIcon.Info);
            }
            base.OnClosing(e);
        }

        // --- НОВОСТИ И СТАТУС ---
        private async Task CheckNewsOnStartup()
        {
            var news = await _githubService.GetNewsAsync();
            if (news != null && news.active)
            {
                Dispatcher.Invoke(() =>
                {
                    NewsText.Text = news.message;
                    try
                    {
                        if (!string.IsNullOrEmpty(news.color))
                            NewsText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(news.color));
                    }
                    catch { }
                    NewsPanel.Visibility = Visibility.Visible;
                });
            }
        }

        // --- УПРАВЛЕНИЕ ОКНОМ ---
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) this.DragMove(); }
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        // Кнопка закрытия вызывает метод Close, который перехватывается в OnClosing
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => this.Close();

        // --- МИНИ РЕЖИМ И ПОДДЕРЖКА ---
        private void MiniModeBtn_Click(object sender, RoutedEventArgs e)
        {
            _isMiniMode = !_isMiniMode;

            if (_isMiniMode)
            {
                LeftColumn.Width = new GridLength(0);
                LeftPanel.Visibility = Visibility.Collapsed;
                TabsPanel.Visibility = Visibility.Collapsed;
                ExtraToolsPanel.Visibility = Visibility.Collapsed;
                ReportBtn.Visibility = Visibility.Collapsed;
                PageSystem.Visibility = Visibility.Collapsed;
                PageConnection.Visibility = Visibility.Visible;
                SupportBtn.Visibility = Visibility.Collapsed;
                LicenseStatsTxt.Visibility = Visibility.Collapsed;
                this.Width = 340;
                this.Height = 420;
                if (sender is Button btn) btn.Content = "🖥️ Полный";
            }
            else
            {
                LeftColumn.Width = new GridLength(310);
                LeftPanel.Visibility = Visibility.Visible;
                TabsPanel.Visibility = Visibility.Visible;
                ExtraToolsPanel.Visibility = Visibility.Visible;
                ReportBtn.Visibility = Visibility.Visible;
                SupportBtn.Visibility = Visibility.Visible;
                LicenseStatsTxt.Visibility = Visibility.Visible;
                this.Width = 800;
                this.Height = 920;
                if (sender is Button btn) btn.Content = "📱 Мини";
            }
        }

        private void SupportBtn_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://t.me/PlayNowSupport", UseShellExecute = true }); } catch { }
        }

        // --- ВКЛАДКИ И АНИМАЦИЯ ---
        private void TabConnect_Click(object sender, RoutedEventArgs e)
        {
            if (PageConnection.Visibility == Visibility.Visible) return;
            AnimateSwitch(PageSystem, PageConnection);
            BtnTabConnect.Style = (Style)FindResource("AccentBtn");
            BtnTabSystem.Style = (Style)FindResource("GlassBtn");
        }

        private void TabSystem_Click(object sender, RoutedEventArgs e)
        {
            if (PageSystem.Visibility == Visibility.Visible) return;
            AnimateSwitch(PageConnection, PageSystem);
            BtnTabConnect.Style = (Style)FindResource("GlassBtn");
            BtnTabSystem.Style = (Style)FindResource("AccentBtn");
        }

        private void AnimateSwitch(UIElement from, UIElement to)
        {
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, e) =>
            {
                from.Visibility = Visibility.Collapsed;
                to.Opacity = 0;
                to.Visibility = Visibility.Visible;
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                to.BeginAnimation(OpacityProperty, fadeIn);
            };
            from.BeginAnimation(OpacityProperty, fadeOut);
        }

        // --- ЗАГРУЗКА И ПИНГ СЕРВЕРОВ ---
        private async Task LoadRealServersAsync()
        {
            ServerListContainer.Children.Clear();
            ServerListContainer.Children.Add(CreateServerCard("Скачивание серверов...", ""));

            _serverData = await _githubService.GetServersAsync();
            _remoteAppsDb = await _githubService.GetAppsDbAsync();

            lock (_pingCache)
            {
                foreach (var key in _serverData.Keys)
                {
                    if (!_pingCache.ContainsKey(key)) _pingCache[key] = "...";
                }
            }

            _ = PingAllServersAsync(useCache: false);
        }

        private async Task<string> GetServerLoadAsync(string ip)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var json = await client.GetStringAsync($"http://{ip}:5678/stats");
                var node = JsonNode.Parse(json);
                if (node != null)
                {
                    var cpu = node["cpu"]?.ToString() ?? "0";
                    var users = node["unique_clients"]?.ToString() ?? node["connections"]?.ToString() ?? "0";
                    return $"CPU: {cpu}% | 👥 {users}";
                }
            }
            catch { return "Load: ERR"; }
            return "Load: ERR";
        }

        private async Task PingAllServersAsync(bool useCache = false)
        {
            if (!useCache)
            {
                var tasks = new List<Task>();
                foreach (var kvp in _serverData)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        string resultStr;
                        if (_showLoadMode) resultStr = await GetServerLoadAsync(kvp.Value);
                        else
                        {
                            int ping = await IcmpPingAsync(kvp.Value, 3000);
                            resultStr = ping >= 1999 || ping <= 0 ? "TIMEOUT" : $"{ping} ms";
                        }
                        lock (_pingCache) { _pingCache[kvp.Key] = resultStr; }
                    }));
                }
                await Task.WhenAll(tasks);
            }

            lock (_pingCache)
            {
                foreach (var key in _serverData.Keys) { if (!_pingCache.ContainsKey(key)) _pingCache[key] = "..."; }
            }

            List<KeyValuePair<string, string>> ordered;
            lock (_pingCache)
            {
                if (_sortMode == "name") ordered = _pingCache.OrderBy(x => x.Key).ToList();
                else if (_sortMode == "load")
                {
                    ordered = _pingCache.OrderBy(x =>
                    {
                        if (x.Value.Contains("👥"))
                        {
                            var parts = x.Value.Split(new[] { "👥" }, StringSplitOptions.None);
                            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int users)) return users;
                        }
                        return 9999;
                    }).ToList();
                }
                else // ping
                {
                    ordered = _pingCache.OrderBy(x =>
                    {
                        if (x.Value.EndsWith("ms") && int.TryParse(x.Value.Replace(" ms", ""), out int p)) return p;
                        return 9999;
                    }).ToList();
                }
            }

            Dispatcher.Invoke(() =>
            {
                ServerListContainer.Children.Clear();
                ServerListContainer.Children.Add(CreateServerCard("Auto", "AUTO"));
                foreach (var s in ordered)
                {
                    if (_serverData.ContainsKey(s.Key)) ServerListContainer.Children.Add(CreateServerCard(s.Key, s.Value));
                }
                UpdateServerSelectionUI();
            });
        }

        // --- ЛОГИКА СОРТИРОВКИ ---
        private string _sortMode = "ping";
        private void SortPing_Click(object sender, RoutedEventArgs e) { _sortMode = "ping"; RefreshServerListUI(); }
        private void SortName_Click(object sender, RoutedEventArgs e) { _sortMode = "name"; RefreshServerListUI(); }
        private void SortLoad_Click(object sender, RoutedEventArgs e) { _sortMode = "load"; RefreshServerListUI(); }
        private void RefreshServerListUI() => _ = PingAllServersAsync(useCache: true);

        private void UpdateServerSelectionUI()
        {
            foreach (UIElement child in ServerListContainer.Children)
            {
                if (child is Border card && card.Child is Grid grid)
                {
                    var titleBlock = grid.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 1);
                    if (titleBlock != null)
                    {
                        bool isSelected = titleBlock.Text == _selectedServer;
                        card.Background = new SolidColorBrush(isSelected ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(13, 255, 255, 255));
                        card.BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(74, 144, 226) : Color.FromArgb(32, 255, 255, 255));
                        card.BorderThickness = new Thickness(isSelected ? 2 : 1);
                    }
                }
            }
        }

        // --- СОЗДАНИЕ КАРТОЧКИ (С ПОДДЕРЖКОЙ ТЕМ И СКАЧИВАНИЕМ ФЛАГОВ) ---
        private Border CreateServerCard(string name, string displayValue)
        {
            bool isSelected = (name == _selectedServer);
            Border card = new Border
            {
                Background = new SolidColorBrush(isSelected ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(13, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(74, 144, 226) : Color.FromArgb(32, 255, 255, 255)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12),
                Cursor = Cursors.Hand
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string appDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string flagPath = System.IO.Path.Combine(appDir, "Resources", "Flags", $"{name.ToLower()}.png");

            // Контейнер для иконки
            Border iconContainer = new Border { Width = 24, Height = 18, Background = Brushes.Transparent };
            Grid.SetColumn(iconContainer, 0);

            if (System.IO.File.Exists(flagPath))
            {
                SetImageIcon(iconContainer, flagPath);
            }
            else
            {
                // Если флага нет, ставим глобус и запускаем фоновое скачивание с GitHub
                SetTextIcon(iconContainer, name);
                _ = DownloadFlagAsync(name, flagPath, iconContainer);
            }

            // Название сервера
            TextBlock title = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            Grid.SetColumn(title, 1);

            // Пинг / Статус
            string colorHex = "#00CA71";
            if (displayValue.Contains("CPU")) colorHex = "#AAAAAA";
            else if (displayValue.Contains("ms") && int.TryParse(displayValue.Replace(" ms", ""), out int p))
            {
                if (p > 100) colorHex = "#FF5A5A"; else if (p > 60) colorHex = "#FFD700";
            }
            if (displayValue == "TIMEOUT" || displayValue == "Load: ERR") colorHex = "#555555";
            if (name == "Auto") colorHex = "#4A90E2";

            TextBlock pingLbl = new TextBlock { Text = displayValue, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)), FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pingLbl, 2);

            grid.Children.Add(iconContainer);
            grid.Children.Add(title);
            grid.Children.Add(pingLbl);
            card.Child = grid;

            card.MouseLeftButtonUp += (s, e) => {
                if (name != "Скачивание серверов...")
                {
                    _selectedServer = name;
                    UpdateServerSelectionUI();
                    if (_serverData.TryGetValue(name, out string? ip) && ip != null) StartPingGraph(ip);
                }
            };
            return card;
        }

        // --- ПОМОЩНИКИ ДЛЯ ИКОНОК ---
        private void SetImageIcon(Border container, string path)
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path); bitmap.EndInit();
            container.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform };
        }

        private void SetTextIcon(Border container, string name)
        {
            var tb = new TextBlock { Text = name == "Auto" ? "⚡" : "🌐", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            container.Child = tb;
        }

        private async Task DownloadFlagAsync(string name, string savePath, Border container)
        {
            if (name == "Auto" || name == "Скачивание серверов...") return;
            try
            {
                // ВАЖНО: Папка Flags на твоем GitHub должна существовать!
                string flagUrl = $"https://raw.githubusercontent.com/yeemelya77/Servers/main/Flags/{name.ToLower()}.png";
                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(flagUrl);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(savePath)!);
                await System.IO.File.WriteAllBytesAsync(savePath, bytes);

                // Обновляем картинку в UI после успешного скачивания
                Dispatcher.Invoke(() => SetImageIcon(container, savePath));
            }
            catch { /* Если на гитхабе нет картинки, просто оставляем глобус */ }
        }

        // --- УВЕДОМЛЕНИЯ И РЕПОРТЫ ---
        private void ShowCustomAlert(string title, string message)
        {
            NotifTitle.Text = title;
            NotifMessage.Text = message;
            NotificationOverlay.Visibility = Visibility.Visible;
        }

        private void Notification_CloseClick(object sender, RoutedEventArgs e) => NotificationOverlay.Visibility = Visibility.Collapsed;

        private void ReportBtn_Click(object sender, RoutedEventArgs e) { ReportOverlay.Visibility = Visibility.Visible; ReportCommentBox.Focus(); }
        private void Report_CancelClick(object sender, RoutedEventArgs e) { ReportOverlay.Visibility = Visibility.Collapsed; ReportCommentBox.Text = ""; }
        private async void Report_SendClick(object sender, RoutedEventArgs e)
        {
            string comment = ReportCommentBox.Text;
            if (string.IsNullOrWhiteSpace(comment)) comment = "Без описания";
            ReportOverlay.Visibility = Visibility.Collapsed;
            ReportCommentBox.Text = "";
            StatusInfo.Text = "📤 Отправка отчета...";
            var reportService = new ReportService();
            bool success = await reportService.SendFullReportAsync(comment);
            if (success) { ShowCustomAlert("Успешно!", "Ваш отчет отправлен."); StatusInfo.Text = "Логи отправлены ✅"; }
            else { ShowCustomAlert("Ошибка", "Не удалось отправить отчет."); StatusInfo.Text = "Ошибка отправки ❌"; }
        }

        // --- VPN ЛОГИКА ---
        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApps == null || _selectedApps.Count == 0) { ShowCustomAlert("Внимание", "Сначала выберите приложения."); return; }
            if (_selectedServer == "Auto") { ShowCustomAlert("Внимание", "Выберите сервер."); return; }
            StatusIcon.Text = "⌛"; StatusIcon.Foreground = Brushes.Gold; StatusText.Text = "ПОДКЛЮЧЕНИЕ...";
            string dnsMode = (DnsSelector.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "System";

            // Читаем значение из нового тумблера
            bool isCdnEnabled = CdnToggle.IsChecked == true;

            var processDb = _remoteAppsDb.ToDictionary(k => k.Key, v => v.Value.Processes);
            bool success = await _vpnManager.ConnectAsync(_selectedServer, _selectedApps, processDb, isCdnEnabled, dnsMode);

            if (success)
            {
                StatusIcon.Text = "🎮"; StatusIcon.Foreground = Brushes.SpringGreen; StatusText.Text = "АКТИВНО"; StatusInfo.Text = $"Туннель работает | {dnsMode} DNS";

                // Включаем техническую сводку
                if (_serverData.TryGetValue(_selectedServer, out string? ip) && ip != null)
                {
                    TechIpTxt.Text = $"IP: {ip}";
                    // Определяем протокол по названию сервера (например "Finland-TCP")
                    TechProtoTxt.Text = _selectedServer.ToLower().Contains("tcp") ? "VLESS (TCP Vision)" : "VLESS (gRPC)";
                    TechInfoPanel.Visibility = Visibility.Visible;
                }
            }
            else { StatusIcon.Text = "❌"; StatusIcon.Foreground = Brushes.Crimson; StatusText.Text = "ОШИБКА"; }
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _vpnManager.Disconnect();
            StatusIcon.Text = "●"; StatusIcon.Foreground = Brushes.Crimson; StatusText.Text = "ОТКЛЮЧЕНО";

            // Прячем техническую сводку
            TechInfoPanel.Visibility = Visibility.Collapsed;
        }
        private void ScanBtn_Click(object sender, RoutedEventArgs e) => _ = LoadRealServersAsync();
        private void ToggleLoadBtn_Click(object sender, RoutedEventArgs e) { _showLoadMode = !_showLoadMode; _pingCache.Clear(); _ = PingAllServersAsync(useCache: false); }

        private void AppsBtn_Click(object sender, RoutedEventArgs e)
        {
            var appsWin = new AppsWindow(_selectedApps, _remoteAppsDb); appsWin.Owner = this;
            if (appsWin.ShowDialog() == true)
            {
                _selectedApps = appsWin.SelectedApps;
                StatusInfo.Text = $"Выбрано игр: {_selectedApps.Count}";
                UpdateTunnelInfoUI(); // <--- Добавили вот эту строчку
            }
        }

        private void SpeedtestBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedServer) || _selectedServer == "Auto") return;
            if (_serverData.TryGetValue(_selectedServer, out string? ip) && ip != null) { var testWin = new SpeedtestWindow(ip); testWin.Owner = this; testWin.ShowDialog(); }
        }

        private async void CheckSitesBtn_Click(object sender, RoutedEventArgs e)
        {
            // 1. Проверяем, подключен ли VPN
            if (!_vpnManager.IsConnected)
            {
                ShowCustomAlert("Ошибка", "Сначала выберите страну и приложение!");
                return;
            }

            // 2. Проверяем, выбраны ли приложения
            if (_selectedApps == null || _selectedApps.Count == 0)
            {
                ShowCustomAlert("Ошибка", "Вы не выбрали ни одной игры для проверки.");
                return;
            }

            StatusInfo.Text = $"🔍 Проверка {_selectedApps.Count} приложений...";
            NotificationOverlay.Visibility = Visibility.Collapsed;

            var reportList = new List<string>();
            int successCount = 0;

            // 3. Проходимся по КАЖДОЙ выбранной игре
            foreach (var appId in _selectedApps)
            {
                // Ищем адрес для проверки. Если нет в базе — пингуем Google как запасной вариант
                string host = _appPingMap.ContainsKey(appId) ? _appPingMap[appId] : "google.com";

                // Получаем красивое название (из словаря меток, если есть, или ID)
                // (Придется немного схитрить, так как словарь меток в другом окне. 
                //  Но мы можем просто использовать ID или сделать запрос проще).
                string displayName = appId.ToUpper();

                // Пингуем (TCP Ping на 443 порт — как реальное соединение)
                bool isAlive = await NetworkHelper.TcpPingAsync(host, 443, 1500) > 0;

                if (isAlive)
                {
                    reportList.Add($"✅ {displayName}");
                    successCount++;
                }
                else
                {
                    reportList.Add($"❌ {displayName}");
                }
            }

            // 4. Формируем красивый отчет
            string finalReport = string.Join("\n", reportList);

            ShowCustomAlert($"Результат ({successCount}/{_selectedApps.Count})", finalReport);
            StatusInfo.Text = $"Доступно: {successCount}/{_selectedApps.Count}";
        }

        private class ServiceResult { public string Category { get; set; } = ""; public string Status { get; set; } = ""; public bool IsOk { get; set; } = false; public override string ToString() => $"{Status} {Category}"; }
        private async Task<ServiceResult> CheckServiceHealthAsync(string name, string[] endpoints)
        {
            int success = 0; foreach (var host in endpoints) { if (await NetworkHelper.TcpPingAsync(host, 443, 1500) > 0) success++; }
            string statusIcon = success == endpoints.Length ? "✅" : (success > 0 ? "⚠️" : "❌");
            string nameDisplay = success > 0 && success < endpoints.Length ? $"{name} (Частично)" : name;
            return new ServiceResult { Category = nameDisplay, Status = statusIcon, IsOk = success > 0 };
        }

        // --- ГРАФИК ---
        private void StartPingGraph(string ip)
        {
            _pingTimer?.Dispose(); _pingTimer = new System.Threading.Timer(async _ => { int ping = await IcmpPingAsync(ip, 2000); Dispatcher.Invoke(() => UpdateGraph(ping)); }, null, 0, 2000);
        }

        private void UpdateGraph(int newPing)
        {
            _pingHistory.RemoveAt(0); _pingHistory.Add(newPing);
            PingCanvas.Children.Clear();
            Polyline line = new Polyline { Stroke = new SolidColorBrush(newPing >= 999 ? Colors.Red : Colors.SpringGreen), StrokeThickness = 2 };
            double step = PingCanvas.ActualWidth / (_pingHistory.Count - 1);
            for (int i = 0; i < _pingHistory.Count; i++)
            {
                double val = Math.Min(_pingHistory[i], 200.0);
                double y = PingCanvas.ActualHeight - (val / 200.0 * PingCanvas.ActualHeight);
                line.Points.Add(new Point(i * step, y));
            }
            PingCanvas.Children.Add(line);
            CurrentPingTxt.Text = newPing >= 999 ? "LOSS" : $"{newPing} ms";
        }

        private void CheckLicenseOnStartup()
        {
            var licService = new LicenseService(); ClubIdTxt.Text = $"ID Клуба: {licService.GetClubId()}";
            if (System.IO.File.Exists(_licenseFilePath)) try { _licenseKey = System.IO.File.ReadAllText(_licenseFilePath).Trim(); } catch { }
            if (string.IsNullOrEmpty(_licenseKey)) ActivationOverlay.Visibility = Visibility.Visible; else _ = VerifyKeyAsync(_licenseKey);
        }

        private async void ActivateBtn_Click(object sender, RoutedEventArgs e) { string key = KeyInput.Text.Trim(); if (!string.IsNullOrEmpty(key)) await VerifyKeyAsync(key); }
        private async Task VerifyKeyAsync(string key)
        {
            var licService = new LicenseService(); var res = await licService.VerifyAsync(key);
            if (res != null && res.ok)
            {
                _licenseKey = key; ActivationOverlay.Visibility = Visibility.Collapsed;
                LicenseStatsTxt.Text = $"Лицензия: {res.active_now?.ToString() ?? "?"} / {res.max_slots?.ToString() ?? "?"}";
                try { System.IO.File.WriteAllText(_licenseFilePath, _licenseKey); } catch { }
                _heartbeatTimer?.Dispose(); _heartbeatTimer = new System.Threading.Timer(async _ => await licService.VerifyAsync(_licenseKey, true), null, 60000, 60000);
            }
            else { ActivationStatusTxt.Text = res?.message ?? "Ошибка сервера"; ActivationOverlay.Visibility = Visibility.Visible; try { if (System.IO.File.Exists(_licenseFilePath)) System.IO.File.Delete(_licenseFilePath); } catch { } }
        }

        private void OpenSoundSettings_Click(object sender, RoutedEventArgs e) => Process.Start("control", "mmsys.cpl,,0");
        private void OpenMouseSettings_Click(object sender, RoutedEventArgs e) => Process.Start("control", "main.cpl,,2");
        private void OpenPowerSettings_Click(object sender, RoutedEventArgs e) => Process.Start("control", "powercfg.cpl");
        private void OpenNvidia_Click(object sender, RoutedEventArgs e) { try { Process.Start("explorer.exe", "shell:AppsFolder\\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel"); } catch { ShowCustomAlert("Ошибка", "Панель NVIDIA не найдена."); } }
        private void MaxRefreshRate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exePath = @"D:\Tools\MonitorResolution.exe";

                // Проверяем, существует ли файл, чтобы программа не крашнулась
                if (System.IO.File.Exists(exePath))
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "-arf",
                        UseShellExecute = true // Позволяет корректно запустить стороннее приложение с UI
                    };

                    System.Diagnostics.Process.Start(startInfo);
                }
                else
                {
                    MessageBox.Show($"Утилита не найдена по пути:\n{exePath}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось запустить утилиту: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void TestMic_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                if (_waveIn == null) { try { _waveIn = new WaveInEvent(); _waveIn.DataAvailable += (s, a) => Dispatcher.Invoke(() => MicLevelBar.Value = (Math.Abs((short)((a.Buffer[1] << 8) | a.Buffer[0])) / 32768f) * 200); _waveIn.StartRecording(); btn.Content = "⏹ Стоп"; btn.Foreground = Brushes.Crimson; } catch { ShowCustomAlert("Ошибка", "Микрофон недоступен"); } }
                else { _waveIn.StopRecording(); _waveIn.Dispose(); _waveIn = null; MicLevelBar.Value = 0; btn.Content = "🎤 Тест"; btn.Foreground = Brushes.White; }
            }
        }

        private async Task<int> IcmpPingAsync(string ip, int timeout = 2000)
        {
            try { using var ping = new Ping(); var reply = await ping.SendPingAsync(ip, timeout); if (reply.Status == IPStatus.Success) return (int)reply.RoundtripTime; } catch { }
            return 9999;
        }
        // --- ТИХОЕ ОБНОВЛЕНИЕ ---
        private async Task CheckUpdatesSilentAsync()
        {
            var updater = new UpdateService();
            // Получаем ссылку, если версия на сервере > текущей
            _updateUrl = await updater.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(_updateUrl))
            {
                Dispatcher.Invoke(() =>
                {
                    // Показываем кнопку в меню "Система"
                    UpdateBtn.Visibility = Visibility.Visible;

                    // Можно также добавить красную точку на вкладку "Система" (по желанию)
                    BtnTabSystem.Content = "🛠️ Система (1)";
                    BtnTabSystem.Foreground = Brushes.Gold;
                });
            }
        }

        private void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_updateUrl))
            {
                new UpdateService().TriggerUpdate(_updateUrl);
            }
        }
        // --- СМЕНА ТЕМЫ ---
        private bool _isDarkTheme = true; // По умолчанию темная

        private void ChangeTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme; // Меняем флаг
            string themeName = _isDarkTheme ? "Dark" : "Light";

            try
            {
                // Загружаем новый словарь
                var newDict = new ResourceDictionary
                {
                    Source = new Uri($"Resources/Themes/{themeName}.xaml", UriKind.Relative)
                };

                // Очищаем старые темы и добавляем новую
                // (При этом стили кнопок обновятся сами, т.к. используют DynamicResource)
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(newDict);
            }
            catch (Exception ex)
            {
                ShowCustomAlert("Ошибка темы", "Не удалось загрузить файл темы.\n" + ex.Message);
            }
        }
        private bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // --- ОБНОВЛЕНИЕ БЛОКА ТУННЕЛЯ ---
        private void UpdateTunnelInfoUI()
        {
            if (_selectedApps == null || _selectedApps.Count == 0)
            {
                ActiveAppsTxt.Text = "Ничего не выбрано";
                ActiveAppsTxt.Foreground = (Brush)FindResource("TextSecondary");
            }
            else
            {
                // Собираем красивые названия игр из словаря
                var appNames = new List<string>();
                foreach (var id in _selectedApps)
                {
                    if (id == "services_cdn") continue; // Пропускаем технический пункт
                    if (_remoteAppsDb.TryGetValue(id, out var config))
                    {
                        // Отрезаем иконку (первые 2 символа), чтобы текст был чище
                        string cleanName = config.Name;
                        if (cleanName.Length > 2 && char.IsSurrogate(cleanName[0]))
                            cleanName = cleanName.Substring(2).Trim();
                        appNames.Add(cleanName);
                    }
                    else
                    {
                        appNames.Add(id);
                    }
                }

                ActiveAppsTxt.Text = string.Join(", ", appNames);
                ActiveAppsTxt.Foreground = (Brush)FindResource("TextPrimary");
            }
        }
    }
}