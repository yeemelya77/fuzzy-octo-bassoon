using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace PlayNow.Client.Services
{
    public class VpnManager
    {
        private readonly VpnService _vpnService;
        private readonly ConfigService _configService;
        private readonly GithubService _githubService;

        public bool IsConnected { get; private set; } = false;
        public string CurrentCountry { get; private set; } = string.Empty;

        public VpnManager()
        {
            _vpnService = new VpnService();
            _configService = new ConfigService();
            _githubService = new GithubService();
        }

        public async Task<bool> ConnectAsync(
            string country,
            List<string> splitApps,
            Dictionary<string, List<string>> remoteAppsDb,
            bool enableCdnUnblock,
            string dnsMode)
        {
            try
            {
                string? rawConfig = await _githubService.DownloadConfigAsync(country);

                if (string.IsNullOrEmpty(rawConfig))
                {
                    // Исправлен текст ошибки, чтобы он больше не сбивал с толку
                    MessageBox.Show(
                        $"ОШИБКА СЕТИ:\nНе удалось скачать профиль конфигурации для сервера: {country}",
                        "Ошибка скачивания с GitHub",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                if (enableCdnUnblock && !splitApps.Contains("services_cdn"))
                {
                    splitApps.Add("services_cdn");
                }

                string readyConfig = _configService.BuildCustomConfig(
                    rawConfig,
                    splitApps,
                    remoteAppsDb,
                    dnsMode);

                bool started = _vpnService.StartVpn(readyConfig);

                if (started)
                {
                    IsConnected = true;
                    CurrentCountry = country;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка контроллера:\n{ex.Message}", "Сбой", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Disconnect()
        {
            _vpnService.StopVpn();
            IsConnected = false;
            CurrentCountry = string.Empty;
        }
    }
}