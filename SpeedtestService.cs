using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlayNow.Client.Services
{
    public class SpeedtestService
    {
        private readonly string _iperfPath;
        private readonly string _workingDir;

        public SpeedtestService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _workingDir = Path.Combine(baseDir, "Resources", "Bin");
            _iperfPath = Path.Combine(_workingDir, "iperf3.exe");
        }

        public async Task<SpeedtestResult> RunTestAsync(string serverIp, int port = 5201)
        {
            // Проверка 1: Путь
            if (!Directory.Exists(_workingDir))
                return new SpeedtestResult { Error = $"Папка не найдена: {_workingDir}" };

            if (!File.Exists(_iperfPath))
                return new SpeedtestResult { Error = $"Файл iperf3.exe не найден по пути: {_iperfPath}" };

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _iperfPath,
                    Arguments = $"-c {serverIp} -p {port} -J -t 5",
                    WorkingDirectory = _workingDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null) return new SpeedtestResult { Error = "Ошибка создания процесса." };

                // Читаем потоки по очереди, чтобы избежать зависания
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrEmpty(output))
                    return new SpeedtestResult { Error = $"iPerf не выдал данных. Ошибка системы: {error}" };

                // Ищем JSON как в твоем backend.py
                int start = output.IndexOf('{');
                int endIdx = output.LastIndexOf('}') + 1;
                if (start == -1) return new SpeedtestResult { Error = "Сервер iPerf не ответил (JSON не найден)." };

                string jsonStr = output.Substring(start, endIdx - start);
                using var doc = JsonDocument.Parse(jsonStr);
                JsonElement root = doc.RootElement;

                // Безопасное чтение свойств
                if (root.TryGetProperty("error", out var err))
                    return new SpeedtestResult { Error = err.GetString() };

                if (!root.TryGetProperty("end", out var endNode))
                    return new SpeedtestResult { Error = "Неполный ответ от сервера." };

                double download = 0;
                if (endNode.TryGetProperty("sum_received", out var recv))
                    download = recv.GetProperty("bits_per_second").GetDouble() / 1_000_000;

                double upload = 0;
                if (endNode.TryGetProperty("sum_sent", out var sent))
                    upload = sent.GetProperty("bits_per_second").GetDouble() / 1_000_000;

                return new SpeedtestResult
                {
                    Success = true,
                    DownloadMbps = Math.Round(download, 2),
                    UploadMbps = Math.Round(upload, 2)
                };
            }
            catch (Exception ex)
            {
                return new SpeedtestResult { Error = ex.Message };
            }
        }
    }

    public class SpeedtestResult
    {
        public bool Success { get; set; }
        public double DownloadMbps { get; set; }
        public double UploadMbps { get; set; }
        public double PingMs { get; set; }
        public string? Error { get; set; }
    }
}