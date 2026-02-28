using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlayNow.Client.Services
{
    public class GithubService
    {
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private readonly string _baseUrl = "https://raw.githubusercontent.com/yeemelya77/Servers/main/";

        // 1. Бронебойное скачивание конфига (Умный поиск)
        public async Task<string?> DownloadConfigAsync(string country)
        {
            try
            {
                string folderName = country; // Например: "Latvia-TCP" или "Russia1"
                var urlsToTry = new List<string>();

                // Если это новый сервер (в названии есть дефис, например "Latvia-TCP")
                if (country.Contains("-"))
                {
                    folderName = country.Split('-')[0]; // Получаем папку: "Latvia"
                    string baseFileName = country.ToLower(); // Получаем: "latvia-tcp", "latvia-grpc", "japan-ws"

                    // Генерируем правильные окончания для GitHub (учитываем gRPC, WS и TCP)
                    string exactFileName = baseFileName
                        .Replace("grpc", "gRPC")
                        .Replace("ws", "WS")
                        .Replace("tcp", "tcp"); // Оставляем tcp маленьким или сделай "TCP", смотря как у тебя на гитхабе

                    // Вариант 1: Папка с большой буквы, специфичный регистр файла (Japan/japan-WS.json)
                    urlsToTry.Add($"{_baseUrl}{folderName}/{exactFileName}.json");

                    // Вариант 2: Папка с большой, всё маленькими (Japan/japan-ws.json)
                    urlsToTry.Add($"{_baseUrl}{folderName}/{baseFileName}.json");

                    // Вариант 3: Папка с маленькой, специфичный регистр (japan/japan-WS.json)
                    urlsToTry.Add($"{_baseUrl}{folderName.ToLower()}/{exactFileName}.json");

                    // Вариант 4: Всё полностью маленькими буквами (japan/japan-ws.json)
                    urlsToTry.Add($"{_baseUrl}{folderName.ToLower()}/{baseFileName}.json");
                }
                else
                {
                    // Для старых серверов (например, Russia1)
                    urlsToTry.Add($"{_baseUrl}{folderName}/config.json");
                    urlsToTry.Add($"{_baseUrl}{folderName.ToLower()}/config.json");
                }

                // Пробуем скачать каждый вариант по очереди.
                foreach (var url in urlsToTry)
                {
                    string fullUrl = $"{url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    var response = await _httpClient.GetAsync(fullUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка скачивания конфига: {ex.Message}");
                return null;
            }
        }

        // 2. Скачивание списка серверов
        public async Task<Dictionary<string, string>> GetServersAsync()
        {
            var serversList = new Dictionary<string, string>();
            try
            {
                string url = $"{_baseUrl}INTERNAL_SERVERS_V2.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                string json = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string name = prop.Name;
                    string ip = prop.Value.GetProperty("ip").GetString() ?? "";
                    serversList.Add(name, ip);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки серверов: {ex.Message}");
                // Заглушка на случай сбоя
                serversList.Add("Finland", "31.57.61.224");
            }
            return serversList;
        }

        // 3. Скачивание базы приложений (ОБНОВЛЕННЫЙ ФОРМАТ)
        public async Task<Dictionary<string, AppConfig>> GetAppsDbAsync()
        {
            try
            {
                string url = $"{_baseUrl}apps_DB_v2.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var json = await _httpClient.GetStringAsync(url);
                return JsonSerializer.Deserialize<Dictionary<string, AppConfig>>(json) ?? new Dictionary<string, AppConfig>();
            }
            catch { return new Dictionary<string, AppConfig>(); }
        }

        // 4. НОВОЕ: Скачивание новостей
        public async Task<NewsData?> GetNewsAsync()
        {
            try
            {
                // Создай файл news.json в репозитории с содержимым: 
                // { "active": true, "message": "⚠️ Техработы в Германии до 15:00", "color": "#FFCC00" }
                string url = $"{_baseUrl}news.json?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var json = await _httpClient.GetStringAsync(url);
                return JsonSerializer.Deserialize<NewsData>(json);
            }
            catch { return null; }
        }
    }

    // Класс для структуры новости
    public class NewsData
    {
        public bool active { get; set; }
        public string message { get; set; } = "";
        public string color { get; set; } = "#FFFFFF"; // Цвет текста (HEX)
    }

    // Класс для структуры игры (Новая база)
    public class AppConfig
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "❓ Другое";
        public List<string> Processes { get; set; } = new List<string>();
    }
}