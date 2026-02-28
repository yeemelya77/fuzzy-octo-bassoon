using System;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlayNow.Client.Services
{
    public class LicenseResponse
    {
        public bool ok { get; set; }
        public string message { get; set; } = "";
        public string expiry { get; set; } = "";
        public int? max_slots { get; set; }
        public int? active_now { get; set; }
    }

    public class LicenseService
    {
        private const string ApiUrl = "https://ru.cvetoshkabotplaynow.ru:8443/verify_license";

        public string GetClubId()
        {
            return Environment.MachineName;
        }

        public async Task<LicenseResponse> VerifyAsync(string key, bool isHeartbeat = false)
        {
            try
            {
                // Собираем полный отпечаток компьютера
                string hwid = GetHardwareId();
                string localIp = GetLocalIpAddress();
                string hostname = Environment.MachineName;

                var payload = new JsonObject
                {
                    ["key"] = key,
                    ["hwid"] = hwid,
                    ["hostname"] = hostname,
                    ["local_ip"] = localIp,
                    ["is_heartbeat"] = isHeartbeat
                };

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                var response = await client.PostAsync(ApiUrl, content);
                string json = await response.Content.ReadAsStringAsync();

                var node = JsonNode.Parse(json);
                if (node != null)
                {
                    return new LicenseResponse
                    {
                        ok = node["ok"]?.GetValue<bool>() ?? false,
                        message = node["message"]?.ToString() ?? "Ошибка",
                        max_slots = node["max_slots"]?.GetValue<int>(),
                        active_now = node["active_now"]?.GetValue<int>()
                    };
                }
            }
            catch (Exception ex)
            {
                return new LicenseResponse { ok = false, message = "Ошибка сети: " + ex.Message };
            }
            return new LicenseResponse { ok = false, message = "Неизвестная ошибка" };
        }

        // --- ФУНКЦИИ СБОРА УНИКАЛЬНЫХ ДАННЫХ ---

        private string GetHardwareId()
        {
            try
            {
                // 1. Пытаемся взять MachineGuid из реестра (уникален для каждой установки Windows)
                using var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = view.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = key?.GetValue("MachineGuid")?.ToString();
                if (!string.IsNullOrEmpty(guid)) return guid;
            }
            catch { }

            try
            {
                // 2. Если реестр недоступен, берем MAC-адрес основной сетевой карты
                var macAddr = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(nic => nic.GetPhysicalAddress().ToString())
                    .FirstOrDefault(mac => !string.IsNullOrEmpty(mac));
                if (!string.IsNullOrEmpty(macAddr)) return macAddr;
            }
            catch { }

            // 3. Последний шанс - имя ПК
            return Environment.MachineName;
        }

        private string GetLocalIpAddress()
        {
            try
            {
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (var ip in netInterface.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                !System.Net.IPAddress.IsLoopback(ip.Address))
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }
    }
}