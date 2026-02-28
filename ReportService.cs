using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace PlayNow.Client.Services
{
    public class ReportService
    {
        private readonly string _binDir;
        // Твои данные из backend.py (убедись, что они верные)
        private readonly string _token = "7897140364:AAFHNQv-T7udWLhySttmKwGBeN6tlp8W4HA";
        private readonly string _chatId = "153140633";

        public ReportService()
        {
            // ИСПРАВЛЕНИЕ ДЛЯ SINGLE FILE EXE:
            // Ищем папку Resources рядом с .exe файлом, а не во временной папке Temp
            string appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            _binDir = Path.Combine(appDir, "Resources", "Bin");
        }

        public async Task<bool> SendFullReportAsync(string comment)
        {
            try
            {
                // Создаем временный архив
                string tempZip = Path.Combine(Path.GetTempPath(), $"report_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

                using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
                {
                    // 1. Лог ядра sing-box (если есть)
                    AddFileToZip(archive, Path.Combine(_binDir, "sing-box.log"), "core_singbox.log");

                    // 2. Текущий конфиг (без ключей, если нужно, но пока шлем целиком для дебага)
                    AddFileToZip(archive, Path.Combine(_binDir, "config.json"), "config.json");

                    // 3. Системная диагностика (Процессы и конфликты)
                    string diagInfo = GetSystemDiagnostics();
                    var diagEntry = archive.CreateEntry("system_diagnostics.txt");
                    using (var writer = new StreamWriter(diagEntry.Open())) { writer.Write(diagInfo); }

                    // 4. Комментарий пользователя и мета-инфо
                    string meta = $"User Comment:\n{comment}\n\n" +
                                  $"Time: {DateTime.Now}\n" +
                                  $"Machine: {Environment.MachineName}\n" +
                                  $"OS: {Environment.OSVersion}";

                    var metaEntry = archive.CreateEntry("report_meta.txt");
                    using (var writer = new StreamWriter(metaEntry.Open())) { writer.Write(meta); }
                }

                // Отправляем в Telegram
                bool success = await UploadToTelegramAsync(tempZip, comment);

                // Чистим мусор
                if (File.Exists(tempZip)) File.Delete(tempZip);

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Report Error: {ex.Message}");
                return false;
            }
        }

        private void AddFileToZip(ZipArchive archive, string path, string entryName)
        {
            if (File.Exists(path))
            {
                try { archive.CreateEntryFromFile(path, entryName); } catch { }
            }
        }

        private string GetSystemDiagnostics()
        {
            var info = new List<string>();
            info.Add($"OS: {Environment.OSVersion}");
            info.Add("\n=== RUNNING PROCESSES ===");

            // Список конфликтов (как в твоем backend.py)
            var conflicts = new List<string> { "winws", "goodbyedpi", "zapret", "warp-svc", "outline_service" };

            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        string name = proc.ProcessName.ToLower();
                        info.Add(name);
                        if (conflicts.Contains(name)) info.Add($"!!! DETECTED CONFLICT: {name} !!!");
                    }
                    catch { }
                }
            }
            catch { info.Add("Error reading processes"); }

            return string.Join("\n", info);
        }

        private async Task<bool> UploadToTelegramAsync(string filePath, string comment)
        {
            using var client = new HttpClient();
            using var content = new MultipartFormDataContent();

            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            content.Add(new ByteArrayContent(fileBytes), "document", Path.GetFileName(filePath));
            content.Add(new StringContent(_chatId), "chat_id");

            // Если комментарий длинный, обрезаем для подписи, полный будет внутри txt
            string caption = comment.Length > 200 ? comment.Substring(0, 200) + "..." : comment;
            content.Add(new StringContent($"🐞 <b>Bug Report</b>\n💬 {caption}"), "caption");
            content.Add(new StringContent("HTML"), "parse_mode");

            var response = await client.PostAsync($"https://api.telegram.org/bot{_token}/sendDocument", content);
            return response.IsSuccessStatusCode;
        }
    }
}