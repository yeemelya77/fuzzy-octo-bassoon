using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PlayNow.Client.Services
{
    public static class NetworkHelper
    {
        // Асинхронный TCP пинг (не вешает интерфейс)
        public static async Task<int> TcpPingAsync(string ip, int port = 443, int timeoutMs = 1500)
        {
            if (string.IsNullOrEmpty(ip)) return 999;
            try
            {
                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();

                // Пытаемся подключиться с таймаутом
                var connectTask = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask)
                {
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
                return 999; // Тайм-аут
            }
            catch { return 999; } // Ошибка сети
        }
    }
}