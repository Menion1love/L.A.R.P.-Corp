using System;
using System.Collections.Generic;
using UnityEngine;
using Project.LarpCore;

namespace Project.LinuxOS
{
    public enum WifiSecurityType
    {
        Open,
        WEP,
        WPA,
        WPA2
    }

    [Serializable]
    public class WifiNetworkInfo
    {
        public string Ssid;
        public string Bssid;
        public int Channel;
        public int SignalBars; // 1..4
        public WifiSecurityType Security;
        public string Password;

        public bool RequiresPassword => Security != WifiSecurityType.Open;
    }

    /// <summary>
    /// Генератор и контроллер Wi-Fi сетей:
    /// - Ровно 1 открытая сеть без пароля
    /// - От 10 до 12 случайных запароленных сетей с реалистичными именами, генерируются заново при каждом запуске
    /// </summary>
    public class WifiSystem
    {
        public static WifiSystem Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private readonly List<WifiNetworkInfo> _networks = new List<WifiNetworkInfo>();
        public IReadOnlyList<WifiNetworkInfo> Networks => _networks;

        public WifiNetworkInfo ConnectedNetwork { get; private set; }
        public event Action OnNetworksChanged;

        private static readonly string[] OpenNetworkCandidates =
        {
            "Home_Free_2.4G",
            "Lobby_Guest_WiFi",
            "Open_Spot_204",
            "CityFree_Hotspot"
        };

        private static readonly string[] RouterPrefixes =
        {
            "TP-LINK_",
            "Keenetic-",
            "MGTS_GPON_",
            "RT-5GPON-",
            "ASUS_5G_",
            "Beeline_2G_",
            "Beeline_5G_",
            "DomRu_5G_",
            "Rostelecom_",
            "HUAWEI-B315-",
            "Xiaomi_AX_",
            "DLink_DIR825_",
            "Netgear_",
            "Linksys_",
            "ZyXEL_",
            "Tenda_5G_"
        };

        private static readonly string[] NeighborNames =
        {
            "Apt_402_5G",
            "KV_19_Home",
            "KV_305_Net",
            "Sosedi_5G",
            "Alexey_Home",
            "Elena_WiFi",
            "Dmitry_5G",
            "Studio_3B",
            "Office_Floor4",
            "Masha_2.4G",
            "Sergey_Net",
            "Floor5_LAN",
            "HomeNet_218",
            "SmartHome_88",
            "Ivanov_5G",
            "Kafe_VseSvoi",
            "Oleg_Router",
            "AndroidAP_84",
            "B410_Private",
            "Workstation_5G"
        };

        private static readonly string[] PasswordPool =
        {
            "qwerty2024", "home5512", "admin8841", "sunset99", "kittens77",
            "mywifi1234", "delta409", "bluebird22", "moscow77", "coffee104",
            "shadow91", "network5g", "pass9021", "router825"
        };

        public WifiSystem()
        {
            Instance = this;
            GenerateSessionNetworks();
        }

        public void GenerateSessionNetworks()
        {
            _networks.Clear();
            ConnectedNetwork = null;
            PlayerState.Instance?.SetWifiDisconnected();

            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            var usedSsids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Ровно одна доступная сеть БЕЗ пароля
            string openSsid = OpenNetworkCandidates[rng.Next(OpenNetworkCandidates.Length)];
            usedSsids.Add(openSsid);
            _networks.Add(new WifiNetworkInfo
            {
                Ssid = openSsid,
                Bssid = GenerateBssid(rng),
                Channel = rng.Next(1, 12),
                SignalBars = 4,
                Security = WifiSecurityType.Open,
                Password = string.Empty
            });

            // 2. От 10 до 12 случайных защищённых сетей при каждом запуске
            int lockedCount = rng.Next(10, 13); // 10, 11 или 12
            var neighborPool = new List<string>(NeighborNames);

            for (int i = 0; i < lockedCount; i++)
            {
                string ssid = GenerateRealisticSsid(rng, neighborPool, usedSsids);
                usedSsids.Add(ssid);

                int secRoll = rng.Next(100);
                WifiSecurityType sec = secRoll < 25 ? WifiSecurityType.WEP
                    : (secRoll < 55 ? WifiSecurityType.WPA : WifiSecurityType.WPA2);

                _networks.Add(new WifiNetworkInfo
                {
                    Ssid = ssid,
                    Bssid = GenerateBssid(rng),
                    Channel = rng.Next(1, 14),
                    SignalBars = rng.Next(1, 5),
                    Security = sec,
                    Password = PasswordPool[rng.Next(PasswordPool.Length)]
                });
            }

            // Перемешиваем защищённые сети, оставляя открытую ближе к верху (в пределах первых 3 позиций)
            // чтобы она естественно смотрелась в списке и не терялась в самом низу
            var openNet = _networks[0];
            var lockedList = _networks.GetRange(1, _networks.Count - 1);
            lockedList.Sort((a, b) => b.SignalBars.CompareTo(a.SignalBars));

            _networks.Clear();
            int openIndex = rng.Next(0, Mathf.Min(3, lockedList.Count + 1));
            for (int i = 0; i < lockedList.Count; i++)
            {
                if (i == openIndex) _networks.Add(openNet);
                _networks.Add(lockedList[i]);
            }
            if (!_networks.Contains(openNet))
                _networks.Add(openNet);

            OnNetworksChanged?.Invoke();
        }

        public bool TryConnect(WifiNetworkInfo network, string password, out string error)
        {
            error = string.Empty;
            if (network == null)
            {
                error = "Сеть не найдена.";
                return false;
            }

            if (network.RequiresPassword && !string.Equals(password ?? string.Empty, network.Password, StringComparison.Ordinal))
            {
                error = "Неверный пароль сети.";
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                return false;
            }

            ConnectedNetwork = network;
            PlayerState.Instance?.SetWifiConnected(network.Ssid, network.SignalBars);
            AudioFeedbackManager.Instance?.PlayClick();
            OnNetworksChanged?.Invoke();
            return true;
        }

        public void Disconnect()
        {
            ConnectedNetwork = null;
            PlayerState.Instance?.SetWifiDisconnected();
            AudioFeedbackManager.Instance?.PlayClick();
            OnNetworksChanged?.Invoke();
        }

        private static string GenerateRealisticSsid(System.Random rng, List<string> neighborPool, HashSet<string> used)
        {
            for (int attempt = 0; attempt < 25; attempt++)
            {
                string candidate;
                if (neighborPool.Count > 0 && rng.Next(100) < 42)
                {
                    int idx = rng.Next(neighborPool.Count);
                    candidate = neighborPool[idx];
                    neighborPool.RemoveAt(idx);
                }
                else
                {
                    string prefix = RouterPrefixes[rng.Next(RouterPrefixes.Length)];
                    string suffix = rng.Next(2) == 0
                        ? rng.Next(1000, 9999).ToString()
                        : $"{rng.Next(0x10, 0xFF):X2}{rng.Next(0x10, 0xFF):X2}";
                    string band = (!prefix.Contains("5G") && !prefix.Contains("2G") && rng.Next(100) < 35) ? "_5G" : "";
                    candidate = prefix + suffix + band;
                }

                if (!used.Contains(candidate))
                    return candidate;
            }

            return $"WLAN_{rng.Next(10000, 99999)}";
        }

        private static string GenerateBssid(System.Random rng)
        {
            return $"{rng.Next(0x10, 0xFE):X2}:{rng.Next(0x10, 0xFE):X2}:{rng.Next(0x10, 0xFE):X2}:" +
                   $"{rng.Next(0x10, 0xFE):X2}:{rng.Next(0x10, 0xFE):X2}:{rng.Next(0x10, 0xFE):X2}";
        }
    }
}
