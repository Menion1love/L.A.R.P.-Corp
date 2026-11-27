using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Linux-подобный рабочий стол L.A.R.P. OS:
    /// - верхний таскбар с часами, вкладками окон, системным треем и Wi-Fi
    /// - рабочий стол с ярлыками (My Computer, Notes, Trash, Mail, Browser, Decoder, Bruteforce, Data Center, DarkNet Market)
    /// - оконный менеджер (перетаскивание, фокус, сворачивание, разворачивание, изменение размера, закрытие)
    /// - виртуальная файловая система и процедурный генератор Wi-Fi сетей при каждом запуске
    /// </summary>
    public class LarpDesktopOS : MonoBehaviour
    {
        public enum AppTab
        {
            Mail,
            Browser,
            Decoder,
            Bruteforce,
            DataCenter,
            SideJobsShop
        }

        private bool _built;

        private OSWindowManager _windowManager;
        private WifiSystem _wifiSystem;
        private VirtualFileSystem _vfs;
        private OSTaskbar _taskbar;

        private RectTransform _desktopIconsLayer;
        private RectTransform _windowsLayer;
        private RectTransform _overlayLayer;

        private readonly List<DesktopIconButton> _desktopIcons = new List<DesktopIconButton>();
        private DesktopIconButton _trashIcon;
        private DesktopIconButton _mailIcon;

        // Активные контроллеры окон приложений
        private NotesWindowApp _notesApp;
        private FileExplorerWindowApp _explorerApp;
        private TrashWindowApp _trashApp;
        private MailAppView _mailView;
        private BrowserAppView _browserView;
        private DecoderAppView _decoderView;
        private BruteforceAppView _bruteforceView;
        private DataCenterAppView _dataCenterView;
        private SideJobsShopAppView _sideJobsView;
        private ProxyAppView _proxyView;

        // Всплывающее системное уведомление (libnotify-стиль в правом нижнем углу)
        private GameObject _notifyPopup;
        private TextMeshProUGUI _notifyText;
        private float _notifyHideAt;

        // Аккуратная полоска подсказки обучения (только во время обучения 1-го дня, в FreeRoam скрыта)
        private GameObject _tutorialHintBar;
        private TextMeshProUGUI _tutorialHintText;

        public WifiSystem Wifi => _wifiSystem;
        public VirtualFileSystem FileSystem => _vfs;

        private void Awake()
        {
            BuildUIIfNeeded();
        }

        private void OnEnable()
        {
            BuildUIIfNeeded();
            SubscribeEvents();
            RefreshAll();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void Update()
        {
            if (_notifyPopup != null && _notifyPopup.activeSelf && Time.unscaledTime > _notifyHideAt)
            {
                _notifyPopup.SetActive(false);
            }
        }

        public void OnDesktopShown()
        {
            BuildUIIfNeeded();
            RefreshAll();
        }

        private void SubscribeEvents()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleGameStateChanged;
                GameManager.Instance.OnToast -= HandleToast;
                GameManager.Instance.OnOpenBrowserUrlRequested -= OpenBrowserAtUrl;

                GameManager.Instance.OnStateChanged += HandleGameStateChanged;
                GameManager.Instance.OnToast += HandleToast;
                GameManager.Instance.OnOpenBrowserUrlRequested += OpenBrowserAtUrl;
            }

            if (PlayerState.Instance != null)
            {
                PlayerState.Instance.OnWifiStateChanged -= HandleWifiChanged;
                PlayerState.Instance.OnWifiStateChanged += HandleWifiChanged;
            }

            if (_vfs != null)
            {
                _vfs.OnFileSystemChanged -= RefreshTrashIcon;
                _vfs.OnFileSystemChanged += RefreshTrashIcon;
            }
        }

        private void UnsubscribeEvents()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= HandleGameStateChanged;
                GameManager.Instance.OnToast -= HandleToast;
                GameManager.Instance.OnOpenBrowserUrlRequested -= OpenBrowserAtUrl;
            }

            if (PlayerState.Instance != null)
            {
                PlayerState.Instance.OnWifiStateChanged -= HandleWifiChanged;
            }

            if (_vfs != null)
            {
                _vfs.OnFileSystemChanged -= RefreshTrashIcon;
            }
        }

        private void BuildUIIfNeeded()
        {
            if (_built) return;
            _built = true;

            PlayerState.EnsureInstance();
            _wifiSystem = new WifiSystem();
            _vfs = new VirtualFileSystem();

            var rootRt = GetComponent<RectTransform>();
            if (rootRt == null) rootRt = gameObject.AddComponent<RectTransform>();
            UITheme.StretchFill(rootRt);

            // 1. Матовый фон рабочего стола (цвет сланца как на референсе)
            var wallpaper = UITheme.CreatePanel(rootRt, "DesktopWallpaper", UITheme.WallpaperSlate);
            UITheme.StretchFill(wallpaper);

            var wpClickBtn = wallpaper.gameObject.AddComponent<Button>();
            wpClickBtn.transition = Selectable.Transition.None;
            wpClickBtn.onClick.AddListener(() =>
            {
                SelectDesktopIcon(null);
                _taskbar?.CloseAllDropdowns();
            });

            // 2. Рабочая область ниже таскбара (top = 38px)
            var desktopArea = UITheme.CreatePanel(rootRt, "DesktopArea", new Color(0, 0, 0, 0));
            UITheme.StretchFill(desktopArea, 0, 0, 0, 38);
            desktopArea.GetComponent<Image>().raycastTarget = false;

            // Слой ярлыков рабочего стола
            _desktopIconsLayer = UITheme.CreatePanel(desktopArea, "DesktopIconsLayer", new Color(0, 0, 0, 0));
            UITheme.StretchFill(_desktopIconsLayer);
            _desktopIconsLayer.GetComponent<Image>().raycastTarget = false;

            // Слой плавающих окон
            _windowsLayer = UITheme.CreatePanel(desktopArea, "WindowsLayer", new Color(0, 0, 0, 0));
            UITheme.StretchFill(_windowsLayer);
            _windowsLayer.GetComponent<Image>().raycastTarget = false;

            _windowManager = new OSWindowManager(_windowsLayer);

            // 3. Верхний таскбар (38px)
            var topBarRt = UITheme.CreatePanel(rootRt, "TopTaskbar", UITheme.TaskbarBg);
            topBarRt.anchorMin = new Vector2(0f, 1f);
            topBarRt.anchorMax = new Vector2(1f, 1f);
            topBarRt.pivot = new Vector2(0.5f, 1f);
            topBarRt.anchoredPosition = Vector2.zero;
            topBarRt.sizeDelta = new Vector2(0f, 38f);

            // 4. Слой оверлеев (выпадающие меню Wi-Fi / Apps / Power + системные уведомления)
            _overlayLayer = UITheme.CreatePanel(rootRt, "OverlayLayer", new Color(0, 0, 0, 0));
            UITheme.StretchFill(_overlayLayer);
            _overlayLayer.GetComponent<Image>().raycastTarget = false;

            _taskbar = topBarRt.gameObject.AddComponent<OSTaskbar>();
            _taskbar.Initialize(this, _windowManager, _wifiSystem, topBarRt, _overlayLayer);

            BuildDesktopIcons();
            BuildNotificationPopup();

            // Открываем окно Notes в левом нижнем углу по умолчанию — как на обоих референсах!
            OpenNotesWindow();
        }

        private void BuildDesktopIcons()
        {
            // Левая колонка: системные + основные приложения
            AddDesktopIcon("My Computer", OSIconFactory.GetDesktopMyComputer(), new Vector2(18f, -18f), OpenMyComputerWindow);
            AddDesktopIcon("Notes", OSIconFactory.GetDesktopNotes(), new Vector2(18f, -130f), OpenNotesWindow);
            _trashIcon = AddDesktopIcon("Trash", OSIconFactory.GetDesktopTrash(_vfs.TrashCount > 0), new Vector2(18f, -242f), OpenTrashWindow);
            _mailIcon = AddDesktopIcon("Mail", OSIconFactory.GetDesktopMail(), new Vector2(18f, -354f), () => SwitchTab(AppTab.Mail));
            AddDesktopIcon("Browser", OSIconFactory.GetDesktopBrowser(), new Vector2(18f, -466f), () => SwitchTab(AppTab.Browser));

            // Вторая колонка: рабочие хакерские утилиты
            AddDesktopIcon("Decoder", OSIconFactory.GetDesktopDecoder(), new Vector2(134f, -18f), () => SwitchTab(AppTab.Decoder));
            AddDesktopIcon("Bruteforce", OSIconFactory.GetDesktopTerminal(), new Vector2(134f, -130f), () => SwitchTab(AppTab.Bruteforce));
            AddDesktopIcon("Data Center", OSIconFactory.GetDesktopDataCenter(), new Vector2(134f, -242f), () => SwitchTab(AppTab.DataCenter));
            AddDesktopIcon("Proxy / VPN", OSIconFactory.GetDesktopProxy(), new Vector2(134f, -354f), OpenProxyWindow);

            // Правая сторона: DarkNet Market (как Shadow Market на референсе)
            var marketIcon = AddDesktopIcon("DarkNet Market", OSIconFactory.GetDesktopMarket(), Vector2.zero, () => SwitchTab(AppTab.SideJobsShop));
            var mrt = marketIcon.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(1f, 1f);
            mrt.anchorMax = new Vector2(1f, 1f);
            mrt.pivot = new Vector2(1f, 1f);
            mrt.anchoredPosition = new Vector2(-22f, -18f);
        }

        private DesktopIconButton AddDesktopIcon(string label, Sprite icon, Vector2 pos, System.Action onOpen)
        {
            var go = new GameObject("Icon_" + label, typeof(RectTransform));
            var comp = go.AddComponent<DesktopIconButton>();
            comp.Initialize(_desktopIconsLayer, label, icon, pos, onOpen, SelectDesktopIcon);
            _desktopIcons.Add(comp);
            return comp;
        }

        private void SelectDesktopIcon(DesktopIconButton selected)
        {
            foreach (var ic in _desktopIcons)
            {
                if (ic != null) ic.SetSelected(ic == selected);
            }
        }

        private void BuildNotificationPopup()
        {
            var box = UITheme.CreatePanel(_overlayLayer, "NotifyPopup", new Color(0.11f, 0.12f, 0.14f, 0.96f));
            box.anchorMin = new Vector2(1f, 0f);
            box.anchorMax = new Vector2(1f, 0f);
            box.pivot = new Vector2(1f, 0f);
            box.anchoredPosition = new Vector2(-18f, 18f);
            box.sizeDelta = new Vector2(460f, 64f);

            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = UITheme.WindowOrange;
            outline.effectDistance = new Vector2(1, -1);

            _notifyText = UITheme.CreateText(box, "Msg", "", 15f, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft);
            UITheme.StretchFill(_notifyText.rectTransform, 14, 8, 14, 8);

            _notifyPopup = box.gameObject;
            _notifyPopup.SetActive(false);

            // Полоска шага обучения (видна только пока идёт обучение 1-го дня)
            var tutBox = UITheme.CreatePanel(_overlayLayer, "TutorialHintBar", new Color(0.09f, 0.10f, 0.12f, 0.94f));
            tutBox.anchorMin = new Vector2(0.5f, 0f);
            tutBox.anchorMax = new Vector2(0.5f, 0f);
            tutBox.pivot = new Vector2(0.5f, 0f);
            tutBox.anchoredPosition = new Vector2(0f, 12f);
            tutBox.sizeDelta = new Vector2(960f, 40f);
            var tutOutline = tutBox.gameObject.AddComponent<Outline>();
            tutOutline.effectColor = UITheme.WindowOrange;
            tutOutline.effectDistance = new Vector2(1, -1);

            _tutorialHintText = UITheme.CreateText(tutBox, "TutTxt", "", 15f, UITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.StretchFill(_tutorialHintText.rectTransform, 14, 4, 14, 4);
            _tutorialHintBar = tutBox.gameObject;
            _tutorialHintBar.SetActive(false);
        }

        public void OpenProxyWindow()
        {
            _windowManager.CreateOrFocusWindow(
                "proxy_vpn",
                "Proxy / VPN",
                new Vector2(680f, 470f),
                UITheme.BgDesktop,
                (content, win) =>
                {
                    _proxyView = content.gameObject.AddComponent<ProxyAppView>();
                    _proxyView.Initialize(content);
                },
                explicitPos: new Vector2(120f, 40f),
                minSize: new Vector2(520f, 360f));
            _proxyView?.Refresh();
        }

        // === ОТКРЫТИЕ СИСТЕМНЫХ ОКОН ===

        public void OpenMyComputerWindow()
        {
            _windowManager.CreateOrFocusWindow(
                "my_computer",
                "My Computer — /home/nullptr",
                new Vector2(820f, 540f),
                UITheme.LightWindowBg,
                (content, win) =>
                {
                    _explorerApp = content.gameObject.AddComponent<FileExplorerWindowApp>();
                    _explorerApp.Initialize(this, _vfs, content);
                },
                explicitPos: new Vector2(-80f, 50f),
                minSize: new Vector2(560f, 360f));
        }

        public void OpenNotesWindow()
        {
            _windowManager.CreateOrFocusWindow(
                "notes",
                "Notes",
                new Vector2(360f, 340f),
                UITheme.LightPaperBg,
                (content, win) =>
                {
                    _notesApp = content.gameObject.AddComponent<NotesWindowApp>();
                    _notesApp.Initialize(content);
                },
                explicitPos: new Vector2(-740f, -310f),
                minSize: new Vector2(280f, 220f));
        }

        public void OpenTrashWindow()
        {
            _windowManager.CreateOrFocusWindow(
                "trash",
                "Trash",
                new Vector2(680f, 420f),
                UITheme.LightWindowBg,
                (content, win) =>
                {
                    _trashApp = content.gameObject.AddComponent<TrashWindowApp>();
                    _trashApp.Initialize(this, _vfs, content);
                },
                explicitPos: new Vector2(120f, -60f),
                minSize: new Vector2(460f, 280f));
        }

        public void OpenTextViewer(string title, string filePath, string contentText)
        {
            string id = "viewer_" + title;
            var existing = _windowManager.FindWindow(id);
            if (existing != null)
            {
                var viewer = existing.ContentRoot.GetComponent<TextViewerWindowApp>();
                viewer?.SetContent(filePath, contentText);
                existing.RestoreFromMinimize();
                return;
            }

            _windowManager.CreateOrFocusWindow(
                id,
                title,
                new Vector2(720f, 520f),
                UITheme.LightPaperBg,
                (content, win) =>
                {
                    var viewer = content.gameObject.AddComponent<TextViewerWindowApp>();
                    viewer.Initialize(content, filePath, contentText);
                },
                explicitPos: new Vector2(60f, 20f),
                minSize: new Vector2(420f, 300f));
        }

        // === ОТКРЫТИЕ ПРИЛОЖЕНИЙ ГЕЙМЛУПА В ВИДЕ ПЕРЕТАСКИВАЕМЫХ ОКОН ===

        public void SwitchTab(AppTab tab)
        {
            switch (tab)
            {
                case AppTab.Mail:
                    _windowManager.CreateOrFocusWindow(
                        "mail",
                        "Mail & Tickets",
                        new Vector2(1060f, 640f),
                        UITheme.BgDesktop,
                        (content, win) =>
                        {
                            _mailView = content.gameObject.AddComponent<MailAppView>();
                            _mailView.Initialize(this, content);
                        },
                        explicitPos: new Vector2(40f, 40f),
                        minSize: new Vector2(760f, 480f));
                    _mailView?.Refresh();
                    break;

                case AppTab.Browser:
                    _windowManager.CreateOrFocusWindow(
                        "browser",
                        "Intranet Browser",
                        new Vector2(1140f, 700f),
                        UITheme.BgDesktop,
                        (content, win) =>
                        {
                            _browserView = content.gameObject.AddComponent<BrowserAppView>();
                            _browserView.Initialize(this, content);
                        },
                        explicitPos: new Vector2(90f, 20f),
                        minSize: new Vector2(820f, 500f));
                    _browserView?.Refresh();
                    break;

                case AppTab.Decoder:
                    _windowManager.CreateOrFocusWindow(
                        "decoder",
                        "Key Decoder",
                        new Vector2(980f, 640f),
                        UITheme.BgDesktop,
                        (content, win) =>
                        {
                            _decoderView = content.gameObject.AddComponent<DecoderAppView>();
                            _decoderView.Initialize(this, content);
                        },
                        explicitPos: new Vector2(-40f, 10f),
                        minSize: new Vector2(740f, 500f));
                    _decoderView?.Refresh();
                    break;

                case AppTab.Bruteforce:
                    _windowManager.CreateOrFocusWindow(
                        "bruteforce",
                        "Bruteforce // Bypass Security",
                        new Vector2(1080f, 700f),
                        UITheme.TerminalBg,
                        (content, win) =>
                        {
                            _bruteforceView = content.gameObject.AddComponent<BruteforceAppView>();
                            _bruteforceView.Initialize(this, content);
                        },
                        explicitPos: new Vector2(20f, -10f),
                        minSize: new Vector2(820f, 540f));
                    _bruteforceView?.Refresh();
                    break;

                case AppTab.DataCenter:
                    _windowManager.CreateOrFocusWindow(
                        "datacenter",
                        "Home Data Center",
                        new Vector2(980f, 640f),
                        UITheme.BgDesktop,
                        (content, win) =>
                        {
                            _dataCenterView = content.gameObject.AddComponent<DataCenterAppView>();
                            _dataCenterView.Initialize(this, content);
                        },
                        explicitPos: new Vector2(70f, -20f),
                        minSize: new Vector2(720f, 460f));
                    _dataCenterView?.Refresh();
                    break;

                case AppTab.SideJobsShop:
                    _windowManager.CreateOrFocusWindow(
                        "market",
                        "DarkNet Market & Jobs",
                        new Vector2(1040f, 660f),
                        UITheme.BgDesktop,
                        (content, win) =>
                        {
                            _sideJobsView = content.gameObject.AddComponent<SideJobsShopAppView>();
                            _sideJobsView.Initialize(this, content);
                        },
                        explicitPos: new Vector2(110f, 30f),
                        minSize: new Vector2(780f, 500f));
                    _sideJobsView?.Refresh();
                    break;
            }
        }

        public void OpenBrowserAtUrl(string url)
        {
            SwitchTab(AppTab.Browser);
            _browserView?.OpenUrl(url);
        }

        public void OpenBruteforceForServer(string serverId)
        {
            SwitchTab(AppTab.Bruteforce);
            _bruteforceView?.SelectServer(serverId);
        }

        private void HandleGameStateChanged()
        {
            SyncParsedFilesToVfs();
            RefreshAll();
        }

        private void HandleWifiChanged()
        {
            _browserView?.Refresh();
            _bruteforceView?.Refresh();
        }

        private void HandleToast(string msg)
        {
            if (_notifyPopup != null && _notifyText != null)
            {
                _notifyText.text = msg;
                _notifyPopup.SetActive(true);
                _notifyPopup.transform.SetAsLastSibling();
                _notifyHideAt = Time.unscaledTime + 4.5f;
            }
        }

        private void SyncParsedFilesToVfs()
        {
            var gm = GameManager.Instance;
            if (gm == null || _vfs == null) return;

            foreach (var srv in gm.Servers.Where(s => s.IsParsed))
            {
                string fileName = $"{srv.Id}_dossier.txt";
                string time = PlayerState.Instance != null ? PlayerState.Instance.GetFormattedTime() : "Сейчас";
                _vfs.AddFile(
                    VirtualFileSystem.DownloadsPath,
                    fileName,
                    VfsEntryType.TextFile,
                    $"{srv.EvidenceTitle}\nСервер: {srv.DisplayName} ({srv.IpAddress})\n\n{srv.EvidenceSummary}",
                    time);
            }
        }

        private void RefreshTrashIcon()
        {
            if (_trashIcon != null && _vfs != null)
            {
                _trashIcon.SetIcon(OSIconFactory.GetDesktopTrash(_vfs.TrashCount > 0));
            }
        }

        public void RefreshAll()
        {
            if (!_built) return;

            RefreshTrashIcon();

            var gm = GameManager.Instance;
            if (gm != null && _mailIcon != null)
            {
                int unread = gm.GetUnreadMailCount();
                _mailIcon.SetLabel(unread > 0 ? $"Mail ({unread})" : "Mail");
            }

            _mailView?.Refresh();
            _browserView?.Refresh();
            _decoderView?.Refresh();
            _bruteforceView?.Refresh();
            _dataCenterView?.Refresh();
            _sideJobsView?.Refresh();
            _proxyView?.Refresh();
            _explorerApp?.Refresh();
            _trashApp?.Refresh();

            if (_tutorialHintBar != null && _tutorialHintText != null)
            {
                bool showTut = Day1StoryController.Instance != null && Day1StoryController.Instance.IsTutorialActive;
                _tutorialHintBar.SetActive(showTut);
                if (showTut)
                    _tutorialHintText.text = Day1StoryController.Instance.GetStageObjectiveText();
            }
        }
    }
}
