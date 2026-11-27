using System;
using UnityEngine;
using Project.LinuxOS;

namespace Project.LarpCore
{
    /// <summary>
    /// Централизованное состояние игрока (PlayerState):
    /// - состояние ПК (включён ли компьютер, залогинен ли пользователь, сидит ли игрок за столом)
    /// - текущее игровое время (день, часы, минуты, формат 12ч/24ч)
    /// - текущее Wi-Fi подключение
    /// - базовые параметры сессии (звук, связь с GameManager)
    /// </summary>
    public class PlayerState : MonoBehaviour
    {
        public static PlayerState Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("Игровое время")]
        [SerializeField] private int _startHour = 8;
        [SerializeField] private int _startMinute = 0;
        [SerializeField] private float _realSecondsPerGameMinute = 3.0f;
        [SerializeField] private bool _use24HourFormat = false;

        private float _minuteAccumulator;
        public bool IsClockTicking { get; set; } = false;

        public bool IsRouterPlaced { get; private set; }
        public string ActiveProxyNode { get; private set; } = "Direct (Home IP)";
        public bool IsProxyActive => ActiveProxyNode != "Direct (Home IP)";

        public event Action OnProxyStateChanged;
        public event Action OnRouterPlacedChanged;

        public int CurrentDay { get; private set; } = 1;
        public int CurrentHour { get; private set; }
        public int CurrentMinute { get; private set; }
        public bool Use24HourFormat => _use24HourFormat;

        public bool IsComputerPoweredOn { get; private set; }
        public bool IsLoggedIntoOS { get; private set; }
        public bool IsAtComputer { get; private set; }
        public bool IsAudioMuted { get; private set; }

        public bool IsWifiConnected => !string.IsNullOrEmpty(ConnectedWifiSsid);
        public string ConnectedWifiSsid { get; private set; } = string.Empty;
        public int ConnectedWifiSignal { get; private set; }

        public event Action OnTimeChanged;
        public event Action OnComputerStateChanged;
        public event Action OnWifiStateChanged;
        public event Action OnAudioMuteChanged;

        public static PlayerState EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<PlayerState>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            var go = new GameObject("PlayerState");
            return go.AddComponent<PlayerState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ResetClockForDay(1);
        }

        private void Update()
        {
            if (!IsClockTicking)
                return;

            if (GameManager.Instance != null && (GameManager.Instance.IsModalOpen || GameManager.Instance.IsGameOver))
                return;

            if (_realSecondsPerGameMinute <= 0.05f)
                return;

            _minuteAccumulator += Time.deltaTime;
            if (_minuteAccumulator >= _realSecondsPerGameMinute)
            {
                int steps = Mathf.FloorToInt(_minuteAccumulator / _realSecondsPerGameMinute);
                _minuteAccumulator -= steps * _realSecondsPerGameMinute;
                AdvanceMinutes(steps);
            }
        }

        public void SetTimeDirect(int day, int hour, int minute, bool ticking)
        {
            CurrentDay = Mathf.Max(1, day);
            CurrentHour = Mathf.Clamp(hour, 0, 23);
            CurrentMinute = Mathf.Clamp(minute, 0, 59);
            _minuteAccumulator = 0f;
            IsClockTicking = ticking;
            OnTimeChanged?.Invoke();
        }

        public void SetRouterPlaced(bool placed)
        {
            IsRouterPlaced = placed;
            OnRouterPlacedChanged?.Invoke();
        }

        public void SetProxyNode(string proxyLabel)
        {
            ActiveProxyNode = string.IsNullOrWhiteSpace(proxyLabel) ? "Direct (Home IP)" : proxyLabel;
            OnProxyStateChanged?.Invoke();
        }

        public void ResetClockForDay(int day)
        {
            CurrentDay = Mathf.Max(1, day);
            CurrentHour = Mathf.Clamp(_startHour, 0, 23);
            CurrentMinute = Mathf.Clamp(_startMinute, 0, 59);
            _minuteAccumulator = 0f;
            OnTimeChanged?.Invoke();
        }

        public void AdvanceMinutes(int minutes)
        {
            if (minutes <= 0) return;

            int total = CurrentHour * 60 + CurrentMinute + minutes;
            int dayOverflow = total / (24 * 60);
            total %= (24 * 60);

            CurrentHour = total / 60;
            CurrentMinute = total % 60;

            if (dayOverflow > 0)
            {
                // Ночь перешла через полночь (00:xx), сам номер игрового дня меняет сон
            }

            OnTimeChanged?.Invoke();
        }

        public void ToggleClockFormat()
        {
            _use24HourFormat = !_use24HourFormat;
            OnTimeChanged?.Invoke();
        }

        public string GetFormattedTime()
        {
            if (_use24HourFormat)
            {
                return $"{CurrentHour:00}:{CurrentMinute:00}";
            }

            string suffix = CurrentHour >= 12 ? "PM" : "AM";
            int hour12 = CurrentHour % 12;
            if (hour12 == 0) hour12 = 12;
            return $"{hour12}:{CurrentMinute:00} {suffix}";
        }

        public void SetComputerPowerState(MonitorPowerController.PowerState state)
        {
            bool powered = state != MonitorPowerController.PowerState.Off;
            bool loggedIn = state == MonitorPowerController.PowerState.Desktop;

            if (IsComputerPoweredOn == powered && IsLoggedIntoOS == loggedIn)
                return;

            IsComputerPoweredOn = powered;
            IsLoggedIntoOS = loggedIn;

            if (!powered)
            {
                SetWifiDisconnected();
            }

            OnComputerStateChanged?.Invoke();
        }

        public void SetAtComputer(bool atComputer)
        {
            if (IsAtComputer == atComputer) return;
            IsAtComputer = atComputer;
            OnComputerStateChanged?.Invoke();
        }

        public void SetWifiConnected(string ssid, int signalStrength)
        {
            ConnectedWifiSsid = ssid ?? string.Empty;
            ConnectedWifiSignal = Mathf.Clamp(signalStrength, 1, 4);
            OnWifiStateChanged?.Invoke();
        }

        public void SetWifiDisconnected()
        {
            if (string.IsNullOrEmpty(ConnectedWifiSsid) && ConnectedWifiSignal == 0)
                return;

            ConnectedWifiSsid = string.Empty;
            ConnectedWifiSignal = 0;
            OnWifiStateChanged?.Invoke();
        }

        public void ToggleMute()
        {
            IsAudioMuted = !IsAudioMuted;
            if (AudioFeedbackManager.Instance != null)
            {
                AudioFeedbackManager.Instance.SetMuted(IsAudioMuted);
            }
            OnAudioMuteChanged?.Invoke();
        }
    }
}
