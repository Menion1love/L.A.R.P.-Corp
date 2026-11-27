using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Верхний таскбар Linux OS (в стиле референсов WTTG2 / Xfce):
    /// слева — меню приложений, баланс и вкладки открытых/свёрнутых окон;
    /// по центру — игровые часы из PlayerState;
    /// справа — Trace, Wi-Fi (с выпадающим списком сетей + диалогом пароля), звук и питание.
    /// </summary>
    public class OSTaskbar : MonoBehaviour
    {
        private LarpDesktopOS _desktop;
        private OSWindowManager _windowManager;
        private WifiSystem _wifiSystem;
        private RectTransform _overlayLayer;

        private RectTransform _windowTabsContainer;
        private TextMeshProUGUI _moneyText;
        private TextMeshProUGUI _clockText;
        private TextMeshProUGUI _traceText;
        private Image _wifiTrayIcon;
        private Image _speakerTrayIcon;

        // Выпадающие меню поверх рабочего стола
        private GameObject _dismissBackdrop;
        private GameObject _wifiDropdown;
        private RectTransform _wifiListContent;
        private GameObject _appsDropdown;
        private GameObject _powerDropdown;

        public void Initialize(
            LarpDesktopOS desktop,
            OSWindowManager windowManager,
            WifiSystem wifiSystem,
            RectTransform topBarRoot,
            RectTransform overlayLayer)
        {
            _desktop = desktop;
            _windowManager = windowManager;
            _wifiSystem = wifiSystem;
            _overlayLayer = overlayLayer;

            BuildTopBar(topBarRoot);
            BuildOverlayMenus();

            _windowManager.OnWindowsChanged += RebuildWindowTabs;
            _wifiSystem.OnNetworksChanged += RefreshWifiUi;

            if (PlayerState.Instance != null)
            {
                PlayerState.Instance.OnTimeChanged += RefreshClock;
                PlayerState.Instance.OnWifiStateChanged += RefreshWifiUi;
                PlayerState.Instance.OnAudioMuteChanged += RefreshAudioIcon;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += RefreshStats;
            }

            RefreshClock();
            RefreshWifiUi();
            RefreshAudioIcon();
            RefreshStats();
            RebuildWindowTabs();
        }

        private void OnDestroy()
        {
            if (_windowManager != null)
                _windowManager.OnWindowsChanged -= RebuildWindowTabs;
            if (_wifiSystem != null)
                _wifiSystem.OnNetworksChanged -= RefreshWifiUi;
            if (PlayerState.Instance != null)
            {
                PlayerState.Instance.OnTimeChanged -= RefreshClock;
                PlayerState.Instance.OnWifiStateChanged -= RefreshWifiUi;
                PlayerState.Instance.OnAudioMuteChanged -= RefreshAudioIcon;
            }
            if (GameManager.Instance != null)
                GameManager.Instance.OnStateChanged -= RefreshStats;
        }

        private void BuildTopBar(RectTransform bar)
        {
            // Тонкая нижняя граница бара
            var bottomBorder = UITheme.CreatePanel(bar, "BottomBorder", new Color(0.03f, 0.03f, 0.04f, 1f));
            bottomBorder.anchorMin = new Vector2(0f, 0f);
            bottomBorder.anchorMax = new Vector2(1f, 0f);
            bottomBorder.pivot = new Vector2(0.5f, 0f);
            bottomBorder.sizeDelta = new Vector2(0f, 1f);

            // --- ЛЕВАЯ СЕКЦИЯ: Apps + баланс + вкладки окон ---
            var leftRow = UITheme.CreatePanel(bar, "LeftSection", new Color(0, 0, 0, 0));
            leftRow.anchorMin = new Vector2(0f, 0f);
            leftRow.anchorMax = new Vector2(0.45f, 1f);
            leftRow.offsetMin = new Vector2(8f, 4f);
            leftRow.offsetMax = new Vector2(0f, -4f);

            var lh = leftRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            lh.spacing = 6;
            lh.childControlWidth = true;
            lh.childControlHeight = true;
            lh.childForceExpandHeight = true;
            lh.childForceExpandWidth = false;

            var appsBtn = CreateIconTrayButton(leftRow, "AppsBtn", OSIconFactory.GetAppsMenuIcon(UITheme.TextPrimary), 32f, () =>
            {
                ToggleDropdown(_appsDropdown);
            });
            UITheme.AddLayout(appsBtn, prefWidth: 32f);

            _moneyText = UITheme.CreateText(leftRow, "Money", "$150", 16f, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, monospace: true);
            UITheme.AddLayout(_moneyText, prefWidth: 74f);

            var vSep = UITheme.CreatePanel(leftRow, "Sep", new Color(1, 1, 1, 0.14f));
            UITheme.AddLayout(vSep, prefWidth: 1f);

            _windowTabsContainer = UITheme.CreatePanel(leftRow, "WindowTabs", new Color(0, 0, 0, 0));
            UITheme.AddLayout(_windowTabsContainer, flexWidth: 1f);
            var th = _windowTabsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 4;
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandHeight = true;
            th.childForceExpandWidth = false;

            // --- ЦЕНТР: Часы (как "10:06 PM" на референсах) ---
            var clockBtn = UITheme.CreateButton(bar, "ClockBtn", "10:00 PM", new Color(0, 0, 0, 0), UITheme.TextPrimary, 19f, () =>
            {
                PlayerState.Instance?.ToggleClockFormat();
            });
            var clockRt = clockBtn.GetComponent<RectTransform>();
            clockRt.anchorMin = new Vector2(0.5f, 0f);
            clockRt.anchorMax = new Vector2(0.5f, 1f);
            clockRt.pivot = new Vector2(0.5f, 0.5f);
            clockRt.sizeDelta = new Vector2(180f, 0f);
            clockRt.anchoredPosition = Vector2.zero;
            _clockText = clockBtn.GetComponentInChildren<TextMeshProUGUI>();

            // --- ПРАВАЯ СЕКЦИЯ (Системный трей): Trace + Wi-Fi + Звук + Питание ---
            var rightRow = UITheme.CreatePanel(bar, "RightSection", new Color(0, 0, 0, 0));
            rightRow.anchorMin = new Vector2(0.65f, 0f);
            rightRow.anchorMax = new Vector2(1f, 1f);
            rightRow.offsetMin = new Vector2(0f, 3f);
            rightRow.offsetMax = new Vector2(-10f, -3f);

            var rh = rightRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 8;
            rh.childAlignment = TextAnchor.MiddleRight;
            rh.childControlWidth = true;
            rh.childControlHeight = true;
            rh.childForceExpandHeight = true;
            rh.childForceExpandWidth = false;

            var traceBtn = UITheme.CreateButton(rightRow, "TraceBtn", "TRACE 0%", new Color(0, 0, 0, 0), UITheme.TextMuted, 14f, () =>
            {
                CloseAllDropdowns();
                _desktop?.OpenProxyWindow();
            }, monospace: true);
            UITheme.AddLayout(traceBtn, prefWidth: 112f);
            _traceText = traceBtn.GetComponentInChildren<TextMeshProUGUI>();
            _traceText.alignment = TextAlignmentOptions.MidlineRight;

            var wifiBtn = CreateIconTrayButton(rightRow, "WifiTrayBtn", OSIconFactory.GetWifiBars(0, UITheme.TextPrimary), 34f, () =>
            {
                ToggleDropdown(_wifiDropdown);
                if (_wifiDropdown.activeSelf) RebuildWifiDropdownList();
            });
            _wifiTrayIcon = wifiBtn.transform.Find("Icon").GetComponent<Image>();

            var spkBtn = CreateIconTrayButton(rightRow, "AudioTrayBtn", OSIconFactory.GetSpeakerIcon(false, UITheme.TextPrimary), 32f, () =>
            {
                PlayerState.Instance?.ToggleMute();
            });
            _speakerTrayIcon = spkBtn.transform.Find("Icon").GetComponent<Image>();

            CreateIconTrayButton(rightRow, "PowerTrayBtn", OSIconFactory.GetPowerIcon(UITheme.TextPrimary), 32f, () =>
            {
                ToggleDropdown(_powerDropdown);
            });
        }

        private Button CreateIconTrayButton(Transform parent, string name, Sprite icon, float width, Action onClick)
        {
            var rt = UITheme.CreatePanel(parent, name, new Color(1, 1, 1, 0f));
            UITheme.AddLayout(rt, prefWidth: width);
            var bg = rt.GetComponent<Image>();

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.normalColor = new Color(1, 1, 1, 0f);
            colors.highlightedColor = new Color(1, 1, 1, 0.14f);
            colors.pressedColor = new Color(0.85f, 0.41f, 0.16f, 0.35f);
            btn.colors = colors;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rt, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            UITheme.StretchFill(iconRt, 4, 3, 4, 3);
            var img = iconGo.GetComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;

            btn.onClick.AddListener(() =>
            {
                AudioFeedbackManager.Instance?.PlayClick();
                onClick?.Invoke();
            });

            return btn;
        }

        private void BuildOverlayMenus()
        {
            // Прозрачный фон для закрытия любого меню по клику мимо
            var backdropRt = UITheme.CreatePanel(_overlayLayer, "DropdownBackdrop", new Color(0, 0, 0, 0.001f));
            UITheme.StretchFill(backdropRt);
            var bdBtn = backdropRt.gameObject.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(CloseAllDropdowns);
            _dismissBackdrop = backdropRt.gameObject;
            _dismissBackdrop.SetActive(false);

            // --- 1. WI-FI DROPDOWN (как на втором референсе: светло-серый список справа сверху!) ---
            var wifiPanel = UITheme.CreatePanel(_overlayLayer, "WifiDropdown", new Color(0.88f, 0.89f, 0.90f, 0.99f));
            wifiPanel.anchorMin = new Vector2(1f, 1f);
            wifiPanel.anchorMax = new Vector2(1f, 1f);
            wifiPanel.pivot = new Vector2(1f, 1f);
            wifiPanel.anchoredPosition = new Vector2(-12f, -40f);
            wifiPanel.sizeDelta = new Vector2(276f, 456f);

            var wifiOutline = wifiPanel.gameObject.AddComponent<Outline>();
            wifiOutline.effectColor = new Color(0.10f, 0.11f, 0.12f, 0.95f);
            wifiOutline.effectDistance = new Vector2(1, -1);

            var wv = wifiPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            wv.spacing = 0;
            wv.padding = new RectOffset(0, 0, 4, 4);
            wv.childControlWidth = true;
            wv.childControlHeight = true;
            wv.childForceExpandWidth = true;
            wv.childForceExpandHeight = false;

            var (wScroll, wContent) = UITheme.CreateVerticalScrollView(wifiPanel, "WifiScroll", new Color(0.88f, 0.89f, 0.90f, 1f), 1f, 3);
            UITheme.AddLayout(wScroll, flexHeight: 1f);
            _wifiListContent = wContent;

            var sep = UITheme.CreatePanel(wifiPanel, "Sep", UITheme.LightDivider);
            UITheme.AddLayout(sep, prefHeight: 1f);

            var discBtn = UITheme.CreateButton(wifiPanel, "DisconnectBtn", "Disconnect...", new Color(0.88f, 0.89f, 0.90f, 1f), UITheme.TextDark, 15f, () =>
            {
                _wifiSystem.Disconnect();
                CloseAllDropdowns();
            });
            UITheme.AddLayout(discBtn, prefHeight: 34f);
            var discLbl = discBtn.GetComponentInChildren<TextMeshProUGUI>();
            discLbl.alignment = TextAlignmentOptions.MidlineLeft;
            discLbl.fontStyle = FontStyles.Normal;
            discLbl.rectTransform.offsetMin = new Vector2(28, 0);

            _wifiDropdown = wifiPanel.gameObject;
            _wifiDropdown.SetActive(false);

            // --- 2. APPS DROPDOWN (слева сверху) ---
            var appsPanel = UITheme.CreatePanel(_overlayLayer, "AppsDropdown", new Color(0.14f, 0.15f, 0.17f, 0.99f));
            appsPanel.anchorMin = new Vector2(0f, 1f);
            appsPanel.anchorMax = new Vector2(0f, 1f);
            appsPanel.pivot = new Vector2(0f, 1f);
            appsPanel.anchoredPosition = new Vector2(8f, -40f);
            appsPanel.sizeDelta = new Vector2(236f, 378f);

            var appsOutline = appsPanel.gameObject.AddComponent<Outline>();
            appsOutline.effectColor = UITheme.WindowBorder;
            appsOutline.effectDistance = new Vector2(1, -1);

            var av = appsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            av.spacing = 2;
            av.padding = new RectOffset(6, 6, 6, 6);
            av.childControlWidth = true;
            av.childControlHeight = true;
            av.childForceExpandWidth = true;
            av.childForceExpandHeight = false;

            AddMenuItem(appsPanel, "My Computer", () => _desktop.OpenMyComputerWindow());
            AddMenuItem(appsPanel, "Notes", () => _desktop.OpenNotesWindow());
            AddMenuItem(appsPanel, "Trash", () => _desktop.OpenTrashWindow());
            UITheme.AddLayout(UITheme.CreatePanel(appsPanel, "SepA", new Color(1, 1, 1, 0.12f)), prefHeight: 1);
            AddMenuItem(appsPanel, "Mail & Tickets", () => _desktop.SwitchTab(LarpDesktopOS.AppTab.Mail));
            AddMenuItem(appsPanel, "Intranet Browser", () => _desktop.SwitchTab(LarpDesktopOS.AppTab.Browser));
            AddMenuItem(appsPanel, "Key Decoder", () => _desktop.SwitchTab(LarpDesktopOS.AppTab.Decoder));
            AddMenuItem(appsPanel, "Bruteforce Terminal", () => _desktop.SwitchTab(LarpDesktopOS.AppTab.Bruteforce));
            AddMenuItem(appsPanel, "Home Data Center", () => _desktop.SwitchTab(LarpDesktopOS.AppTab.DataCenter));
            AddMenuItem(appsPanel, "Proxy / VPN", () => _desktop.OpenProxyWindow());
            AddMenuItem(appsPanel, "DarkNet Market", () => _desktop.SwitchTab(LarpDesktopOS.AppTab.SideJobsShop));

            _appsDropdown = appsPanel.gameObject;
            _appsDropdown.SetActive(false);

            // --- 3. POWER DROPDOWN (справа сверху) ---
            var powerPanel = UITheme.CreatePanel(_overlayLayer, "PowerDropdown", new Color(0.14f, 0.15f, 0.17f, 0.99f));
            powerPanel.anchorMin = new Vector2(1f, 1f);
            powerPanel.anchorMax = new Vector2(1f, 1f);
            powerPanel.pivot = new Vector2(1f, 1f);
            powerPanel.anchoredPosition = new Vector2(-8f, -40f);
            powerPanel.sizeDelta = new Vector2(230f, 122f);

            var pOutline = powerPanel.gameObject.AddComponent<Outline>();
            pOutline.effectColor = UITheme.WindowBorder;
            pOutline.effectDistance = new Vector2(1, -1);

            var pv = powerPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            pv.spacing = 2;
            pv.padding = new RectOffset(6, 6, 6, 6);
            pv.childControlWidth = true;
            pv.childControlHeight = true;
            pv.childForceExpandWidth = true;
            pv.childForceExpandHeight = false;

            AddMenuItem(powerPanel, "Встать из-за ПК [ESC]", () => PlayerStateController.Instance?.ExitComputer());
            AddMenuItem(powerPanel, "Лечь спать (Итоги дня)", () =>
            {
                PlayerStateController.Instance?.ExitComputer();
                GameManager.Instance?.SleepAndEndDay();
            });
            AddMenuItem(powerPanel, "Выключить ПК", () =>
            {
                PlayerStateController.Instance?.MonitorPower?.TurnOff();
            });

            _powerDropdown = powerPanel.gameObject;
            _powerDropdown.SetActive(false);
        }

        private void AddMenuItem(Transform parent, string label, Action action)
        {
            var btn = UITheme.CreateButton(parent, "Item_" + label, label, new Color(0, 0, 0, 0), UITheme.TextPrimary, 15f, () =>
            {
                CloseAllDropdowns();
                action?.Invoke();
            });
            UITheme.AddLayout(btn, prefHeight: 33f);
            var colors = btn.colors;
            colors.normalColor = new Color(1, 1, 1, 0f);
            colors.highlightedColor = UITheme.WindowOrange;
            colors.pressedColor = UITheme.WindowOrangeHover;
            btn.colors = colors;
            btn.GetComponent<Image>().color = Color.white;

            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.fontStyle = FontStyles.Normal;
            lbl.rectTransform.offsetMin = new Vector2(12, 0);
        }

        private void ToggleDropdown(GameObject target)
        {
            bool willOpen = !target.activeSelf;
            CloseAllDropdowns();
            if (willOpen)
            {
                _dismissBackdrop.SetActive(true);
                target.SetActive(true);
                target.transform.SetAsLastSibling();
            }
        }

        public void CloseAllDropdowns()
        {
            if (_dismissBackdrop != null) _dismissBackdrop.SetActive(false);
            if (_wifiDropdown != null) _wifiDropdown.SetActive(false);
            if (_appsDropdown != null) _appsDropdown.SetActive(false);
            if (_powerDropdown != null) _powerDropdown.SetActive(false);
        }

        private void RebuildWifiDropdownList()
        {
            if (_wifiListContent == null || _wifiSystem == null) return;
            UITheme.ClearChildren(_wifiListContent);

            var connected = _wifiSystem.ConnectedNetwork;
            foreach (var net in _wifiSystem.Networks)
            {
                bool isConnected = connected != null && connected.Ssid == net.Ssid;

                var row = UITheme.CreatePanel(_wifiListContent, "Wifi_" + net.Ssid, isConnected ? new Color(0.80f, 0.82f, 0.85f, 1f) : new Color(0.88f, 0.89f, 0.90f, 1f));
                UITheme.AddLayout(row, prefHeight: 31f);

                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = row.GetComponent<Image>();
                var colors = btn.colors;
                colors.highlightedColor = new Color(0.75f, 0.78f, 0.82f, 1f);
                colors.pressedColor = new Color(0.68f, 0.72f, 0.77f, 1f);
                btn.colors = colors;

                WifiNetworkInfo captured = net;
                btn.onClick.AddListener(() =>
                {
                    AudioFeedbackManager.Instance?.PlayClick();
                    CloseAllDropdowns();

                    if (!captured.RequiresPassword)
                    {
                        _wifiSystem.TryConnect(captured, string.Empty, out _);
                        GameManager.Instance?.PostToast($"Wi-Fi подключён: {captured.Ssid}");
                    }
                    else
                    {
                        OpenWifiPasswordWindow(captured);
                    }
                });

                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 6;
                h.padding = new RectOffset(8, 8, 2, 2);
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandHeight = true;
                h.childForceExpandWidth = false;

                var checkGo = new GameObject("Check", typeof(RectTransform), typeof(Image));
                checkGo.transform.SetParent(row, false);
                var checkImg = checkGo.GetComponent<Image>();
                checkImg.sprite = OSIconFactory.GetCheckmarkIcon(UITheme.TextDark);
                checkImg.preserveAspect = true;
                checkImg.enabled = isConnected;
                checkImg.raycastTarget = false;
                UITheme.AddLayout(checkImg, prefWidth: 16f);

                var ssidTxt = UITheme.CreateText(row, "Ssid", net.Ssid, 15f, UITheme.TextDark, TextAlignmentOptions.MidlineLeft, isConnected ? FontStyles.Bold : FontStyles.Normal);
                UITheme.AddLayout(ssidTxt, flexWidth: 1f);

                // Замочек только у запароленных сетей (у единственной открытой сети замка нет!)
                var lockGo = new GameObject("Lock", typeof(RectTransform), typeof(Image));
                lockGo.transform.SetParent(row, false);
                var lockImg = lockGo.GetComponent<Image>();
                lockImg.sprite = OSIconFactory.GetLockIcon(UITheme.TextDark);
                lockImg.preserveAspect = true;
                lockImg.enabled = net.RequiresPassword;
                lockImg.raycastTarget = false;
                UITheme.AddLayout(lockImg, prefWidth: 16f);

                var sigGo = new GameObject("Signal", typeof(RectTransform), typeof(Image));
                sigGo.transform.SetParent(row, false);
                var sigImg = sigGo.GetComponent<Image>();
                sigImg.sprite = OSIconFactory.GetWifiBars(net.SignalBars, UITheme.TextDark);
                sigImg.preserveAspect = true;
                sigImg.raycastTarget = false;
                UITheme.AddLayout(sigImg, prefWidth: 22f);
            }
        }

        private void OpenWifiPasswordWindow(WifiNetworkInfo network)
        {
            _windowManager.CreateOrFocusWindow(
                "wifi_auth",
                $"Wi-Fi Authentication — {network.Ssid}",
                new Vector2(430f, 225f),
                UITheme.LightWindowBg,
                (content, win) =>
                {
                    UITheme.ClearChildren(content);
                    var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
                    v.spacing = 10;
                    v.padding = new RectOffset(16, 16, 14, 14);
                    v.childControlWidth = true;
                    v.childControlHeight = true;
                    v.childForceExpandWidth = true;
                    v.childForceExpandHeight = false;

                    var info = UITheme.CreateText(content, "Info",
                        $"Сеть <b>{network.Ssid}</b> ({network.Security}) защищена паролем.",
                        15f, UITheme.TextDark);
                    UITheme.AddLayout(info, prefHeight: 28f);

                    var passInput = UITheme.CreateInputField(content, "PassInput", "Введите пароль...", 15f, lightStyle: true, monospace: true);
                    passInput.contentType = TMP_InputField.ContentType.Password;
                    UITheme.AddLayout(passInput, prefHeight: 36f);

                    var errText = UITheme.CreateText(content, "Err", "", 14f, UITheme.AccentRed);
                    UITheme.AddLayout(errText, prefHeight: 22f);

                    var btnRow = UITheme.CreatePanel(content, "Btns", new Color(0, 0, 0, 0));
                    UITheme.AddLayout(btnRow, prefHeight: 36f);
                    var bh = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                    bh.spacing = 10;
                    bh.childAlignment = TextAnchor.MiddleRight;
                    bh.childControlWidth = true;
                    bh.childControlHeight = true;
                    bh.childForceExpandHeight = true;
                    bh.childForceExpandWidth = false;

                    void Submit()
                    {
                        if (_wifiSystem.TryConnect(network, passInput.text, out string err))
                        {
                            GameManager.Instance?.PostToast($"Wi-Fi подключён: {network.Ssid}");
                            win.Close();
                        }
                        else
                        {
                            errText.text = err;
                        }
                    }

                    passInput.onSubmit.AddListener(_ => Submit());

                    var cancelBtn = UITheme.CreateButton(btnRow, "Cancel", "Отмена", UITheme.LightToolbarBg, UITheme.TextDark, 14f, () => win.Close());
                    UITheme.AddLayout(cancelBtn, prefWidth: 100f);

                    var okBtn = UITheme.CreateButton(btnRow, "Connect", "Подключиться", UITheme.WindowOrange, UITheme.TextPrimary, 14f, Submit);
                    UITheme.AddLayout(okBtn, prefWidth: 130f);
                },
                explicitPos: Vector2.zero,
                allowMaximize: false,
                allowResize: false);
        }

        private void RebuildWindowTabs()
        {
            if (_windowTabsContainer == null || _windowManager == null) return;
            UITheme.ClearChildren(_windowTabsContainer);

            foreach (var win in _windowManager.Windows)
            {
                if (win == null || !win.IsOpen) continue;

                bool focused = win == _windowManager.FocusedWindow && !win.IsMinimized;
                Color bg = focused ? UITheme.TaskbarTabActive : UITheme.TaskbarTabIdle;
                Color fg = win.IsMinimized ? UITheme.TextMuted : UITheme.TextPrimary;

                OSWindow captured = win;
                var tabBtn = UITheme.CreateButton(_windowTabsContainer, "Tab_" + win.WindowId, win.Title, bg, fg, 13f, () =>
                {
                    _windowManager.ToggleFromTaskbar(captured);
                });
                UITheme.AddLayout(tabBtn, prefWidth: 138f);

                var lbl = tabBtn.GetComponentInChildren<TextMeshProUGUI>();
                lbl.textWrappingMode = TextWrappingModes.NoWrap;
                lbl.overflowMode = TextOverflowModes.Truncate;
                lbl.fontStyle = focused ? FontStyles.Bold : FontStyles.Normal;

                if (focused)
                {
                    var accent = UITheme.CreatePanel(tabBtn.transform, "FocusBar", UITheme.WindowOrange);
                    accent.anchorMin = new Vector2(0f, 0f);
                    accent.anchorMax = new Vector2(1f, 0f);
                    accent.pivot = new Vector2(0.5f, 0f);
                    accent.sizeDelta = new Vector2(0f, 2f);
                }
            }
        }

        private void RefreshClock()
        {
            if (_clockText != null && PlayerState.Instance != null)
            {
                _clockText.text = PlayerState.Instance.GetFormattedTime();
            }
        }

        private void RefreshWifiUi()
        {
            if (_wifiTrayIcon != null && _wifiSystem != null)
            {
                int bars = _wifiSystem.ConnectedNetwork != null ? _wifiSystem.ConnectedNetwork.SignalBars : 0;
                _wifiTrayIcon.sprite = OSIconFactory.GetWifiBars(bars, UITheme.TextPrimary);
            }

            if (_wifiDropdown != null && _wifiDropdown.activeSelf)
            {
                RebuildWifiDropdownList();
            }
        }

        private void RefreshAudioIcon()
        {
            if (_speakerTrayIcon != null && PlayerState.Instance != null)
            {
                _speakerTrayIcon.sprite = OSIconFactory.GetSpeakerIcon(PlayerState.Instance.IsAudioMuted, UITheme.TextPrimary);
            }
        }

        private void RefreshStats()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            if (_moneyText != null)
                _moneyText.text = $"${gm.Money}";

            if (_traceText != null)
            {
                _traceText.text = $"TRACE {gm.Trace}%";
                _traceText.color = gm.Trace >= 65 ? UITheme.AccentRed : (gm.Trace >= 25 ? UITheme.AccentAmber : UITheme.TextMuted);
            }
        }
    }
}
