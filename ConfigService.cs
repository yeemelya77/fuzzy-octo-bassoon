using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PlayNow.Client.Services
{
    public class ConfigService
    {
        public string BuildCustomConfig(string rawJson, List<string> selectedAppsKeys, Dictionary<string, List<string>> remoteAppsDb, string dnsMode = "System")
        {
            JsonNode? config = JsonNode.Parse(rawJson);
            if (config == null) throw new Exception("Не удалось распарсить базовый конфиг.");

            List<string> targetProcesses = new List<string>();
            bool enableCdnUnblock = false;

            foreach (var key in selectedAppsKeys)
            {
                if (key == "services_cdn") enableCdnUnblock = true;
                else if (remoteAppsDb != null && remoteAppsDb.ContainsKey(key))
                {
                    targetProcesses.AddRange(remoteAppsDb[key]);
                }
                else
                {
                    targetProcesses.Add(key);
                }
            }

            ConfigureDnsAndInbounds(config, dnsMode);

            // Получаем адрес VLESS
            string vlessAddress = "";
            var outbounds = config["outbounds"]?.AsArray();
            if (outbounds != null)
            {
                foreach (var outb in outbounds)
                {
                    if (outb?["tag"]?.ToString() == "proxy" && outb?["type"]?.ToString() == "vless")
                    {
                        vlessAddress = outb["server"]?.ToString() ?? "";
                        break;
                    }
                }
            }

            var rulesArray = config["route"]?["rules"]?.AsArray();
            if (rulesArray != null)
            {
                rulesArray.Clear();

                // Базовые правила
                rulesArray.Add(new JsonObject { ["protocol"] = "dns", ["outbound"] = "dns-out" });
                rulesArray.Add(new JsonObject { ["ip_is_private"] = true, ["outbound"] = "direct" });
                rulesArray.Add(new JsonObject { ["protocol"] = "quic", ["port"] = 443, ["outbound"] = "block" });

                if (!string.IsNullOrEmpty(vlessAddress))
                {
                    rulesArray.Add(new JsonObject { ["domain"] = new JsonArray(vlessAddress), ["outbound"] = "direct" });
                    if (System.Net.IPAddress.TryParse(vlessAddress, out _))
                        rulesArray.Add(new JsonObject { ["ip_cidr"] = new JsonArray(vlessAddress + "/32"), ["outbound"] = "direct" });
                }

                // Геймерские правила (только если юзер сам выбрал эту игру)
                if (selectedAppsKeys.Contains("pubg"))
                {
                    rulesArray.Add(new JsonObject { ["ip_cidr"] = new JsonArray("18.200.0.0/14", "3.0.0.0/9", "52.0.0.0/10", "54.0.0.0/10", "34.192.0.0/10", "62.115.0.0/16", "208.70.72.0/22", "198.39.128.0/18", "64.94.0.0/19"), ["outbound"] = "proxy" });
                    rulesArray.Add(new JsonObject { ["domain_suffix"] = new JsonArray(".vivox.com", ".vivox.net", ".pubg.com"), ["outbound"] = "proxy" });
                    rulesArray.Add(new JsonObject { ["port_range"] = new JsonArray("10000:20000", "27000:27100", "50000:65000"), ["network"] = "udp", ["outbound"] = "proxy" });
                }

                if (selectedAppsKeys.Contains("repo"))
                {
                    rulesArray.Add(new JsonObject { ["port"] = new JsonArray(5055, 5056, 5057, 5058, 27000, 27001, 27002, 27003), ["network"] = "udp", ["outbound"] = "proxy" });
                    rulesArray.Add(new JsonObject { ["port"] = new JsonArray(9090, 9091, 9092, 9093, 19090, 19091), ["network"] = "tcp", ["outbound"] = "proxy" });
                    rulesArray.Add(new JsonObject { ["geosite"] = new JsonArray("amazon", "aws"), ["outbound"] = "proxy" });
                }

                if (enableCdnUnblock)
                {
                    rulesArray.Add(new JsonObject { ["geosite"] = new JsonArray("cloudflare", "cloudfront", "amazon", "aws", "fastly"), ["outbound"] = "proxy" });
                }

                // Процессы
                if (targetProcesses.Count > 0)
                {
                    // УДАЛЕНА СТРОКА С ЖЕСТКОЙ ПРИВЯЗКОЙ YOUTUBE/DISCORD И Т.Д.

                    var processArray = new JsonArray();
                    foreach (var proc in targetProcesses.Distinct())
                        processArray.Add(proc.EndsWith(".exe") ? proc : proc + ".exe");

                    var dnsRules = config["dns"]?["rules"]?.AsArray();
                    if (dnsRules != null)
                    {
                        dnsRules.Insert(0, new JsonObject { ["process_name"] = processArray.DeepClone(), ["server"] = "remote_dns" });
                    }
                    rulesArray.Add(new JsonObject { ["process_name"] = processArray, ["outbound"] = "proxy" });
                }
            }

            if (outbounds != null)
            {
                if (!HasTag(outbounds, "block")) outbounds.Add(new JsonObject { ["type"] = "block", ["tag"] = "block" });
                if (!HasTag(outbounds, "direct")) outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = "direct" });
            }

            if (config["route"] != null) config["route"]["final"] = "direct";

            return config.ToString();
        }

        private void ConfigureDnsAndInbounds(JsonNode config, string dnsMode)
        {
            if (dnsMode != "System" && dnsMode != "default")
            {
                string newDns = dnsMode.ToLower() switch
                {
                    "google" => "8.8.8.8",
                    "nextdns" => "45.90.28.190",
                    "adguard" => "94.140.14.14",
                    _ => "1.1.1.1"
                };

                var dnsServers = config["dns"]?["servers"]?.AsArray();
                if (dnsServers != null)
                {
                    foreach (var server in dnsServers)
                    {
                        if (server?["detour"]?.ToString() == "proxy" || server?["tag"]?.ToString().Contains("remote") == true)
                            server["address"] = newDns;
                    }
                }
            }

            var inbounds = config["inbounds"]?.AsArray();
            if (inbounds != null)
            {
                foreach (var inbound in inbounds)
                {
                    if (inbound?["type"]?.ToString() == "tun")
                    {
                        // Оптимизация: MTU 1400 для баланса стабильности и скорости загрузки
                        inbound["mtu"] = 1400;
                        inbound["stack"] = "system";
                        inbound["endpoint_independent_nat"] = true;

                        // Стандартные параметры
                        inbound["sniff"] = true;
                        inbound["sniff_override_destination"] = true;
                        inbound["auto_route"] = true;
                        inbound["strict_route"] = true;

                        // Удаляем http_proxy, если он был в шаблоне
                        if (inbound["platform"] != null)
                        {
                            inbound.AsObject().Remove("platform");
                        }
                    }
                }
            }
        }

        private bool HasTag(JsonArray array, string tag)
        {
            foreach (var item in array) if (item?["tag"]?.ToString() == tag) return true;
            return false;
        }
    }
}