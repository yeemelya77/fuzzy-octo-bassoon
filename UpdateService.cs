using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlayNow.Client.Services
{
    public class UpdateService
    {
        // ТЕКУЩАЯ ВЕРСИЯ ПРОГРАММЫ
        public const string CurrentVersion = "2.0.1";

        private readonly string _versionUrl = "https://raw.githubusercontent.com/yeemelya77/Servers/main/version.json";

        // Возвращает ссылку на скачивание, если есть новая версия. Иначе null.
        public async Task<string?> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                // Качаем JSON с версией
                var json = await client.GetStringAsync(_versionUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var node = JsonNode.Parse(json);

                string remoteVersionStr = node?["version"]?.ToString() ?? "0.0.0";
                string downloadUrl = node?["url"]?.ToString() ?? "";

                // Парсим версии в числа (чтобы 2.0.1 было больше 1.9.0)
                Version local = Version.Parse(CurrentVersion);
                Version remote = Version.Parse(remoteVersionStr);

                // Логика: Предлагаем обнову ТОЛЬКО если удаленная версия СТРОГО БОЛЬШЕ
                if (remote > local && !string.IsNullOrEmpty(downloadUrl))
                {
                    return downloadUrl;
                }
            }
            catch
            {
                // Ошибки игнорируем (нет инета - нет обнов)
            }
            return null;
        }

        public void TriggerUpdate(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }
    }
}