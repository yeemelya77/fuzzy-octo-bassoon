using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PlayNow.Client.Services
{
    public class VpnService
    {
        private Process? _vpnProcess;

        private string GetAppDirectory()
        {
            return Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        public bool StartVpn(string configJson)
        {
            try
            {
                StopVpn();

                string appDir = GetAppDirectory();
                string binDir = Path.Combine(appDir, "Resources", "Bin");
                string exePath = Path.Combine(binDir, "sing-box.exe");
                string configPath = Path.Combine(binDir, "config.json");
                string logPath = Path.Combine(binDir, "sing-box.log");

                if (!File.Exists(exePath))
                    throw new FileNotFoundException($"Не найден файл ядра:\n{exePath}");

                // Очищаем старый лог перед новым запуском, чтобы файл не весил гигабайты
                if (File.Exists(logPath))
                {
                    try { File.Delete(logPath); } catch { }
                }

                File.WriteAllText(configPath, configJson);

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "run -c config.json",
                    WorkingDirectory = binDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                _vpnProcess = new Process { StartInfo = startInfo };

                // Пишем логи в файл
                _vpnProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) try { File.AppendAllText(logPath, e.Data + "\n"); } catch { } };
                _vpnProcess.OutputDataReceived += (s, e) => { if (e.Data != null) try { File.AppendAllText(logPath, e.Data + "\n"); } catch { } };

                _vpnProcess.Start();
                _vpnProcess.BeginErrorReadLine();
                _vpnProcess.BeginOutputReadLine();

                // Ждем и проверяем
                Thread.Sleep(500);

                if (_vpnProcess.HasExited)
                {
                    string error = "Ядро завершилось аварийно.";
                    if (File.Exists(logPath))
                    {
                        try { error += "\nЛог:\n" + File.ReadAllText(logPath); } catch { }
                    }
                    throw new Exception(error);
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка запуска ядра: {ex.Message}");
            }
        }

        public void StopVpn()
        {
            try
            {
                if (_vpnProcess != null && !_vpnProcess.HasExited)
                {
                    _vpnProcess.Kill();
                    _vpnProcess = null;
                }
                foreach (var proc in Process.GetProcessesByName("sing-box"))
                {
                    try { proc.Kill(); } catch { }
                }
            }
            catch { }
        }
    }
}