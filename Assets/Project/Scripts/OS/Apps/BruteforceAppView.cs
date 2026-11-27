using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Bruteforce & Обход защитной системы» — Шаги 4 и 5 Core Loop:
    /// ввод собранного ключа, запуск брутфорса с прогресс-баром, мини-игра обхода узлов на время,
    /// рост Trace при ошибках/затягивании и «Аварийный сброс сессии».
    /// </summary>
    public class BruteforceAppView : MonoBehaviour
    {
        private static readonly string[] SafeSignatures =
        {
            "TCP:443 // OPEN",
            "SSH:22 // PASS",
            "TLS:8443 // AUTH",
            "DNS:53 // ALLOW",
            "API:8080 // ROOT",
            "VPN:1194 // TUN"
        };

        private static readonly string[] TrapSignatures =
        {
            "TCP:445 // ICE",
            "SSH:2222 // TRAP",
            "TLS:8444 // LOCK",
            "UDP:5353 // DENY",
            "API:8081 // HONEY",
            "FW:9001 // TRACE",
            "SMB:139 // GUARD",
            "RDP:3389 // BLOCK"
        };

        private LarpDesktopOS _desktop;
        private RectTransform _serverSelectRow;
        private TextMeshProUGUI _serverInfoText;
        private TMP_InputField _keyInput;
        private Button _pasteKeyBtn;
        private Button _startBruteBtn;
        private Button _abortBtn;
        private TextMeshProUGUI _statusBanner;

        private Image _downloadFill;
        private TextMeshProUGUI _downloadLabel;
        private TextMeshProUGUI _timerText;
        private TextMeshProUGUI _targetSigText;
        private RectTransform _gridContainer;

        private string _selectedServerId = "srv_eye_01";
        private bool _isBreaching;
        private int _totalSteps = 4;
        private int _currentStep;
        private float _timeRemaining;
        private float _maxTime;
        private int _lastTickSecond = -1;
        private string _currentTargetSig;

        private class NodeCell
        {
            public int Col;
            public string Signature;
            public bool IsSafe;
            public Button Btn;
            public Image Bg;
            public TextMeshProUGUI Label;
            public bool Solved;
            public bool Failed;
        }

        private readonly List<NodeCell> _cells = new List<NodeCell>();

        public void Initialize(LarpDesktopOS desktop, RectTransform root)
        {
            _desktop = desktop;

            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12;
            v.padding = new RectOffset(18, 18, 14, 14);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var title = UITheme.CreateText(root, "Title", "BRUTEFORCE & ОБХОД ЗАЩИТНОЙ СИСТЕМЫ (BYPASS SECURITY)", 23, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(title, prefHeight: 34);

            // Выбор сервера
            _serverSelectRow = UITheme.CreatePanel(root, "SrvRow", new Color(0, 0, 0, 0));
            UITheme.AddLayout(_serverSelectRow, prefHeight: 46);
            var sh = _serverSelectRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            sh.spacing = 12;
            sh.childControlWidth = true;
            sh.childControlHeight = true;
            sh.childForceExpandWidth = true;
            sh.childForceExpandHeight = true;

            // Строка ввода ключа и запуска
            var controlPanel = UITheme.CreatePanel(root, "ControlPanel", UITheme.BgPanel);
            UITheme.AddLayout(controlPanel, prefHeight: 112);
            var cv = controlPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.spacing = 8;
            cv.padding = new RectOffset(14, 14, 10, 10);
            cv.childControlWidth = true;
            cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;

            _serverInfoText = UITheme.CreateText(controlPanel, "SrvInfo", "", 17, UITheme.TextMuted);
            UITheme.AddLayout(_serverInfoText, prefHeight: 28);

            var keyRow = UITheme.CreatePanel(controlPanel, "KeyRow", new Color(0, 0, 0, 0));
            UITheme.AddLayout(keyRow, prefHeight: 52);
            var kh = keyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            kh.spacing = 10;
            kh.childControlWidth = true;
            kh.childControlHeight = true;
            kh.childForceExpandHeight = true;
            kh.childForceExpandWidth = false;

            _keyInput = UITheme.CreateInputField(keyRow, "KeyInput", "Введите ключ (например, LARP-7419-ROOT)...", 19);
            UITheme.AddLayout(_keyInput, flexWidth: 1);

            _pasteKeyBtn = UITheme.CreateButton(keyRow, "PasteBtn", "Вставить из Декодера", UITheme.BgCardHover, UITheme.AccentCyan, 16, PasteAssembledKey);
            UITheme.AddLayout(_pasteKeyBtn, prefWidth: 250);

            _startBruteBtn = UITheme.CreateButton(keyRow, "StartBtn", "ЗАПУСТИТЬ BRUTEFORCE", UITheme.AccentGreen, UITheme.TextDark, 17, StartBreachAttempt);
            UITheme.AddLayout(_startBruteBtn, prefWidth: 270);

            _abortBtn = UITheme.CreateButton(keyRow, "AbortBtn", "АВАРИЙНЫЙ СБРОС", UITheme.AccentRed, UITheme.TextPrimary, 16, EmergencyAbort);
            UITheme.AddLayout(_abortBtn, prefWidth: 220);

            // Прогресс скачивания + таймер
            var telemetryRow = UITheme.CreatePanel(root, "TelemetryRow", UITheme.BgPanel);
            UITheme.AddLayout(telemetryRow, prefHeight: 58);
            var th = telemetryRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 16;
            th.padding = new RectOffset(14, 14, 10, 10);
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandHeight = true;
            th.childForceExpandWidth = false;

            var (barRoot, barFill, barLabel) = UITheme.CreateProgressBar(telemetryRow, "DownloadBar", UITheme.BgDesktop, UITheme.AccentCyan, "Скачивание: 0%", 17);
            UITheme.AddLayout(barRoot, flexWidth: 1);
            _downloadFill = barFill;
            _downloadLabel = barLabel;

            _timerText = UITheme.CreateText(telemetryRow, "Timer", "ТАЙМЕР: --:--", 24, UITheme.AccentAmber, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.AddLayout(_timerText, prefWidth: 240);

            // Поле мини-игры Bypass Security
            var gamePanel = UITheme.CreatePanel(root, "BypassPanel", UITheme.BgDesktop);
            UITheme.AddLayout(gamePanel, flexHeight: 1);
            var gv = gamePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            gv.spacing = 12;
            gv.padding = new RectOffset(18, 18, 14, 14);
            gv.childControlWidth = true;
            gv.childControlHeight = true;
            gv.childForceExpandWidth = true;
            gv.childForceExpandHeight = false;

            _statusBanner = UITheme.CreateText(gamePanel, "Banner", "Вставьте ключ доступа и запустите Bruteforce, чтобы начать обход защиты.", 19, UITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.AddLayout(_statusBanner, prefHeight: 34);

            _targetSigText = UITheme.CreateText(gamePanel, "TargetSig", "", 22, UITheme.AccentGreen, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.AddLayout(_targetSigText, prefHeight: 38);

            _gridContainer = UITheme.CreatePanel(gamePanel, "NodeGrid", new Color(0.04f, 0.08f, 0.07f, 1f));
            UITheme.AddLayout(_gridContainer, flexHeight: 1);
            var gh = _gridContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            gh.spacing = 18;
            gh.padding = new RectOffset(22, 22, 20, 20);
            gh.childControlWidth = true;
            gh.childControlHeight = true;
            gh.childForceExpandWidth = true;
            gh.childForceExpandHeight = true;

            Refresh();
        }

        public void SelectServer(string serverId)
        {
            if (_isBreaching) return;
            _selectedServerId = serverId;
            if (GameManager.Instance != null && GameManager.Instance.IsServerKeyComplete(serverId))
            {
                _keyInput.text = GameManager.Instance.GetAssembledKey(serverId);
            }
            Refresh();
        }

        private void Update()
        {
            if (!_isBreaching) return;

            var gm = GameManager.Instance;
            if (gm == null || gm.IsGameOver)
            {
                _isBreaching = false;
                Refresh();
                return;
            }

            _timeRemaining -= Time.deltaTime;
            int sec = Mathf.CeilToInt(Mathf.Max(0f, _timeRemaining));

            if (_timerText != null)
            {
                _timerText.text = $"ТАЙМЕР: 00:{sec:00}";
                _timerText.color = sec <= 6 ? UITheme.AccentRed : (sec <= 12 ? UITheme.AccentAmber : UITheme.AccentGreen);
            }

            if (sec <= 5 && sec > 0 && sec != _lastTickSecond)
            {
                _lastTickSecond = sec;
                AudioFeedbackManager.Instance?.PlayTimerTick();
            }

            if (_timeRemaining <= 0f)
            {
                _isBreaching = false;
                gm.AddTrace(22, "Истёк таймер обхода файрвола");
                _statusBanner.text = "<color=#FF4B5C>ВРЕМЯ ВЫШЛО! Защита сбросила соединение (+22% Trace). Попробуйте снова или очистите логи.</color>";
                _targetSigText.text = "";
                UITheme.SetBarFill(_downloadFill, 0f);
                _downloadLabel.text = "Скачивание прервано (таймаут)";
                Refresh();
            }
        }

        private void PasteAssembledKey()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (!gm.IsServerKeyComplete(_selectedServerId))
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                gm.PostToast("Ключ ещё не собран полностью! Сначала найдите и декодируйте все 3 фрагмента.");
                return;
            }

            _keyInput.text = gm.GetAssembledKey(_selectedServerId);
            gm.PostToast($"Ключ {_keyInput.text} вставлен в Bruteforce.");
        }

        private void StartBreachAttempt()
        {
            var gm = GameManager.Instance;
            if (gm == null || _isBreaching) return;

            if (PlayerState.Instance != null && !PlayerState.Instance.IsWifiConnected)
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                _statusBanner.text = "<color=#D14343>Нет подключения к Wi-Fi! Подключитесь к открытой сети в правом верхнем углу таскбара.</color>";
                return;
            }

            if (Day1StoryController.Instance != null && !Day1StoryController.Instance.CanStartBruteforceNow(out string tutBlock))
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                _statusBanner.text = $"<color=#D14343>{tutBlock}</color>";
                gm.PostToast(tutBlock);
                return;
            }

            var srv = gm.Servers.FirstOrDefault(s => s.Id == _selectedServerId);
            if (!gm.CanStartHack(srv, out string blockReason))
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                _statusBanner.text = $"<color=#FF4B5C>{blockReason}</color>";
                gm.PostToast(blockReason);
                return;
            }

            string entered = (_keyInput.text ?? string.Empty).Trim();
            if (!string.Equals(entered, srv.RequiredKey, StringComparison.OrdinalIgnoreCase))
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                _statusBanner.text = "<color=#FF4B5C>НЕВЕРНЫЙ КЛЮЧ ДОСТУПА! Соберите и расшифруйте все 3 части ключа в «Декодере».</color>";
                gm.PostToast("Неверный ключ доступа для выбранного сервера.");
                return;
            }

            // Запускаем мини-игру обхода защиты! Чем выше сложность и Trace, тем меньше времени и больше столбцов
            _isBreaching = true;
            _totalSteps = Mathf.Clamp(3 + srv.Difficulty, 4, 5);
            _currentStep = 0;
            float tracePenaltySec = gm.Trace * 0.08f;
            _maxTime = Mathf.Clamp(32f - (srv.Difficulty - 1) * 4f - tracePenaltySec, 14f, 32f);
            _timeRemaining = _maxTime;
            _lastTickSecond = -1;

            UITheme.SetBarFill(_downloadFill, 0f);
            _downloadLabel.text = $"Скачивание {srv.IpAddress}: 0% (обход 0/{_totalSteps})";
            _statusBanner.text = "<color=#FFB84D>ЗАЩИТА КОРПОРАЦИИ АКТИВИРОВАНА! Проведите сигнал от START к ACCESS по открытому порту!</color>";

            BuildMinigameGrid();
            UpdateActiveStepPrompt();
            RefreshControlsState();
        }

        private void EmergencyAbort()
        {
            if (!_isBreaching) return;
            _isBreaching = false;
            UITheme.ClearChildren(_gridContainer);
            _cells.Clear();
            UITheme.SetBarFill(_downloadFill, 0f);
            _downloadLabel.text = "Сессия экстренно сброшена";
            _timerText.text = "ТАЙМЕР: --:--";
            _targetSigText.text = "";
            _statusBanner.text = "<color=#00D4FF>АВАРИЙНЫЙ СБРОС СЕССИИ: соединение разорвано без роста Trace. Прогресс скачивания потерян.</color>";
            GameManager.Instance?.PostToast("Аварийный сброс сессии выполнен. Тревога не выросла.");
            Refresh();
        }

        private void BuildMinigameGrid()
        {
            UITheme.ClearChildren(_gridContainer);
            _cells.Clear();

            // Колонка START
            var startCol = UITheme.CreatePanel(_gridContainer, "StartCol", new Color(0.06f, 0.22f, 0.14f, 1f));
            UITheme.AddLayout(startCol, prefWidth: 110);
            var startTxt = UITheme.CreateText(startCol, "Txt", "START\n>", 20, UITheme.AccentGreen, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.StretchFill(startTxt.rectTransform);

            // Столбцы узлов
            var rng = new System.Random();
            for (int col = 0; col < _totalSteps; col++)
            {
                var colPanel = UITheme.CreatePanel(_gridContainer, "Col_" + col, new Color(0.06f, 0.10f, 0.13f, 0.7f));
                UITheme.AddLayout(colPanel, flexWidth: 1);
                var colV = colPanel.gameObject.AddComponent<VerticalLayoutGroup>();
                colV.spacing = 14;
                colV.padding = new RectOffset(10, 10, 12, 12);
                colV.childControlWidth = true;
                colV.childControlHeight = true;
                colV.childForceExpandWidth = true;
                colV.childForceExpandHeight = true;

                var colHdr = UITheme.CreateText(colPanel, "Hdr", $"УЗЕЛ #{col + 1}", 15, UITheme.TextMuted, TextAlignmentOptions.Center, FontStyles.Bold);
                UITheme.AddLayout(colHdr, prefHeight: 24);

                int safeRow = rng.Next(0, 3);
                string safeSig = SafeSignatures[rng.Next(0, SafeSignatures.Length)];
                var usedTraps = new HashSet<string>();

                for (int row = 0; row < 3; row++)
                {
                    bool isSafe = row == safeRow;
                    string sig;
                    if (isSafe)
                    {
                        sig = safeSig;
                    }
                    else
                    {
                        do { sig = TrapSignatures[rng.Next(0, TrapSignatures.Length)]; }
                        while (!usedTraps.Add(sig));
                    }

                    var nodeBtn = UITheme.CreateButton(colPanel, $"Node_{col}_{row}", sig, UITheme.BgCard, UITheme.TextPrimary, 16, null);
                    var cell = new NodeCell
                    {
                        Col = col,
                        Signature = sig,
                        IsSafe = isSafe,
                        Btn = nodeBtn,
                        Bg = nodeBtn.GetComponent<Image>(),
                        Label = nodeBtn.GetComponentInChildren<TextMeshProUGUI>()
                    };
                    _cells.Add(cell);

                    NodeCell captured = cell;
                    nodeBtn.onClick.AddListener(() => OnNodeClicked(captured));
                }
            }

            // Колонка ACCESS
            var endCol = UITheme.CreatePanel(_gridContainer, "AccessCol", new Color(0.10f, 0.14f, 0.22f, 1f));
            UITheme.AddLayout(endCol, prefWidth: 120);
            var endTxt = UITheme.CreateText(endCol, "Txt", ">\nACCESS", 20, UITheme.AccentCyan, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.StretchFill(endTxt.rectTransform);

            UpdateColumnVisuals();
        }

        private void UpdateActiveStepPrompt()
        {
            var safeCell = _cells.FirstOrDefault(c => c.Col == _currentStep && c.IsSafe);
            _currentTargetSig = safeCell != null ? safeCell.Signature : "";
            _targetSigText.text = $"ШАГ {_currentStep + 1}/{_totalSteps} — ВЫБЕРИТЕ ОТКРЫТЫЙ УЗЕЛ:  <color=#00D4FF>[ {_currentTargetSig} ]</color>";
        }

        private void UpdateColumnVisuals()
        {
            foreach (var cell in _cells)
            {
                if (cell.Solved)
                {
                    cell.Bg.color = new Color(0.10f, 0.48f, 0.26f, 1f);
                    cell.Label.color = UITheme.TextPrimary;
                    cell.Btn.interactable = false;
                }
                else if (cell.Failed)
                {
                    cell.Bg.color = new Color(0.55f, 0.12f, 0.16f, 1f);
                    cell.Label.color = UITheme.TextPrimary;
                    cell.Btn.interactable = false;
                }
                else if (cell.Col == _currentStep)
                {
                    cell.Bg.color = new Color(0.16f, 0.24f, 0.34f, 1f);
                    cell.Label.color = UITheme.TextPrimary;
                    cell.Btn.interactable = true;
                }
                else
                {
                    cell.Bg.color = new Color(0.08f, 0.10f, 0.14f, 0.85f);
                    cell.Label.color = UITheme.TextMuted;
                    cell.Btn.interactable = false;
                }
            }
        }

        private void OnNodeClicked(NodeCell cell)
        {
            if (!_isBreaching || cell.Col != _currentStep)
                return;

            var gm = GameManager.Instance;
            if (gm == null) return;

            if (cell.IsSafe)
            {
                cell.Solved = true;
                _currentStep++;
                AudioFeedbackManager.Instance?.PlayClueFound();

                float progress = (float)_currentStep / _totalSteps;
                UITheme.SetBarFill(_downloadFill, progress);
                _downloadLabel.text = $"Скачивание: {Mathf.RoundToInt(progress * 100f)}% (обход {_currentStep}/{_totalSteps})";

                if (_currentStep >= _totalSteps)
                {
                    // Победа!
                    _isBreaching = false;
                    UpdateColumnVisuals();
                    _targetSigText.text = "<color=#2EE676>[OK] ACCESS GRANTED! Сырые данные отправлены в Домашний Дата-Центр!</color>";
                    _statusBanner.text = "<color=#2EE676>ВЗЛОМ УСПЕШЕН! Откройте вкладку «Дата-Центр», чтобы отслеживать парсинг и выбрать путь.</color>";
                    _timerText.text = "ГОТОВО";
                    _timerText.color = UITheme.AccentGreen;

                    gm.CompleteHackAndStartParsing(_selectedServerId);
                    RefreshControlsState();
                    return;
                }

                UpdateActiveStepPrompt();
                UpdateColumnVisuals();
            }
            else
            {
                // Ошибка: ловушка ICE!
                cell.Failed = true;
                _timeRemaining = Mathf.Max(0.5f, _timeRemaining - 2f);
                gm.AddTrace(18, $"Ошибка узла ({cell.Signature})");
                _statusBanner.text = $"<color=#FF4B5C>СРАБОТАЛА ЛОВУШКА {cell.Signature}! Trace +18%, время -2с! Ищите: {_currentTargetSig}</color>";
                UpdateColumnVisuals();
            }
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            if (gm == null || _serverSelectRow == null) return;

            var visible = gm.Servers.Where(s => s.UnlockDay <= gm.Day).ToList();
            if (visible.All(s => s.Id != _selectedServerId))
                _selectedServerId = visible.FirstOrDefault()?.Id;

            UITheme.ClearChildren(_serverSelectRow);
            foreach (var srv in visible)
            {
                bool active = srv.Id == _selectedServerId;
                string tag = srv.IsHacked ? " [СКАЧАН]" : (gm.IsServerKeyComplete(srv.Id) ? " [КЛЮЧ ГОТОВ]" : " [НЕТ КЛЮЧА]");
                string idCopy = srv.Id;
                var btn = UITheme.CreateButton(
                    _serverSelectRow,
                    "BSrv_" + srv.Id,
                    srv.DisplayName + tag,
                    active ? UITheme.AccentCyan : UITheme.BgCard,
                    active ? UITheme.TextDark : UITheme.TextPrimary,
                    16,
                    () =>
                    {
                        if (!_isBreaching)
                        {
                            _selectedServerId = idCopy;
                            if (gm.IsServerKeyComplete(idCopy))
                                _keyInput.text = gm.GetAssembledKey(idCopy);
                            else
                                _keyInput.text = string.Empty;
                            Refresh();
                        }
                    });
                btn.interactable = !_isBreaching;
            }

            var currentSrv = visible.FirstOrDefault(s => s.Id == _selectedServerId);
            if (currentSrv != null)
            {
                string keyState = gm.IsServerKeyComplete(currentSrv.Id)
                    ? $"<color=#2EE676>Ключ собран: {gm.GetAssembledKey(currentSrv.Id)}</color>"
                    : "<color=#FFB84D>Ключ ещё не собран (проверьте Браузер и Декодер)</color>";
                _serverInfoText.text = $"IP: {currentSrv.IpAddress}  |  Сложность: {currentSrv.Difficulty}/3  |  Усталость: +{currentSrv.FatigueCost}%  |  {keyState}";

                if (!_isBreaching && currentSrv.IsHacked)
                {
                    UITheme.ClearChildren(_gridContainer);
                    var doneBox = UITheme.CreatePanel(_gridContainer, "DoneBox", UITheme.BgPanel);
                    var dv = doneBox.gameObject.AddComponent<VerticalLayoutGroup>();
                    dv.spacing = 16;
                    dv.padding = new RectOffset(30, 30, 30, 30);
                    dv.childAlignment = TextAnchor.MiddleCenter;
                    dv.childControlWidth = true;
                    dv.childControlHeight = true;

                    var txt = UITheme.CreateText(doneBox, "DoneInfo", $"[OK] Сервер {currentSrv.DisplayName} уже взломан.\nСырые данные переданы в Домашний Дата-Центр.", 22, UITheme.AccentGreen, TextAlignmentOptions.Center, FontStyles.Bold);
                    UITheme.AddLayout(txt, prefHeight: 80);

                    var goDc = UITheme.CreateButton(doneBox, "GoDC", "Перейти в Домашний Дата-Центр →", UITheme.AccentCyan, UITheme.TextDark, 20, () => _desktop.SwitchTab(LarpDesktopOS.AppTab.DataCenter));
                    UITheme.AddLayout(goDc, prefHeight: 58, prefWidth: 440);
                }
            }

            RefreshControlsState();
        }

        private void RefreshControlsState()
        {
            var gm = GameManager.Instance;
            var srv = gm?.Servers.FirstOrDefault(s => s.Id == _selectedServerId);
            bool alreadyHacked = srv != null && srv.IsHacked;

            if (_startBruteBtn != null) _startBruteBtn.interactable = !_isBreaching && !alreadyHacked;
            if (_pasteKeyBtn != null) _pasteKeyBtn.interactable = !_isBreaching && !alreadyHacked;
            if (_keyInput != null) _keyInput.interactable = !_isBreaching && !alreadyHacked;
            if (_abortBtn != null) _abortBtn.interactable = _isBreaching;
        }
    }
}
