using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Project.LarpCore;
using Project.LinuxOS;

namespace Project.UI
{
    /// <summary>
    /// Минималистичное главное меню игры в стиле референса (Welcome to the Game), на чистом чёрном фоне (#000000):
    /// NEW GAME / CONTINUE / OPTIONS / QUIT + минималистичная панель настроек.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        public static MainMenuController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private GameObject _rootPanel;
        private GameObject _mainButtonsGroup;
        private GameObject _optionsPanel;
        private Image _liveDot;
        private TextMeshProUGUI _viewersText;
        private Button _continueBtn;
        private TextMeshProUGUI _continueLabel;

        // Опции
        private TextMeshProUGUI _volumeValueText;
        private TextMeshProUGUI _clockValueText;
        private TextMeshProUGUI _displayValueText;
        private float _masterVolume = 1f;

        private bool _hasActiveSession;
        private int _viewerCount = 4608;
        private float _viewerTickTimer;

        public bool IsOpen => _rootPanel != null && _rootPanel.activeSelf;

        public static MainMenuController EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<MainMenuController>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            var go = new GameObject("MainMenuCanvas");
            return go.AddComponent<MainMenuController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildMenuCanvas();
        }

        private void Start()
        {
            ShowMenu(initialLaunch: true);
        }

        private void Update()
        {
            if (!IsOpen)
            {
                // Открытие меню паузы по Esc, когда игрок в комнате и нет открытых модалок
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    bool atPC = PlayerStateController.Instance != null &&
                                (PlayerStateController.Instance.IsAtComputer || PlayerStateController.Instance.IsFlying);
                    bool placing = RouterPlacementController.Instance != null && RouterPlacementController.Instance.IsPlacing;
                    bool modal = GameManager.Instance != null && GameManager.Instance.IsModalOpen;

                    if (!atPC && !placing && !modal)
                    {
                        ShowMenu(initialLaunch: false);
                    }
                }
                return;
            }

            // Мягкое мерцание красной точки LIVE и живой счётчик наблюдателей
            if (_liveDot != null)
            {
                float alpha = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f));
                _liveDot.color = new Color(0.86f, 0.12f, 0.12f, alpha);
            }

            _viewerTickTimer += Time.unscaledDeltaTime;
            if (_viewerTickTimer >= 2.2f)
            {
                _viewerTickTimer = 0f;
                _viewerCount = Mathf.Clamp(_viewerCount + UnityEngine.Random.Range(-14, 19), 4200, 5100);
                if (_viewersText != null)
                    _viewersText.text = _viewerCount.ToString("N0");
            }

            if (_hasActiveSession && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (_optionsPanel != null && _optionsPanel.activeSelf)
                    ShowMainButtons();
                else
                    ResumeSession();
            }
        }

        private void BuildMenuCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            var rootRt = GetComponent<RectTransform>();

            // Чисто чёрный фон (#000000) как просил пользователь
            var bg = UITheme.CreatePanel(rootRt, "BlackBackground", Color.black);
            UITheme.StretchFill(bg);
            _rootPanel = bg.gameObject;

            // Ненавязчивая верхняя полоса LIVE + счётчик наблюдателей (как на 1-м фото)
            var liveRow = UITheme.CreatePanel(bg, "LiveStrip", new Color(0, 0, 0, 0));
            liveRow.anchorMin = new Vector2(0.5f, 0.5f);
            liveRow.anchorMax = new Vector2(0.5f, 0.5f);
            liveRow.pivot = new Vector2(0.5f, 0.5f);
            liveRow.sizeDelta = new Vector2(1180f, 34f);
            liveRow.anchoredPosition = new Vector2(0f, 220f);

            // Слева: красная точка + LIVE
            var leftLive = UITheme.CreatePanel(liveRow, "LeftLive", new Color(0, 0, 0, 0));
            leftLive.anchorMin = new Vector2(0f, 0f);
            leftLive.anchorMax = new Vector2(0f, 1f);
            leftLive.pivot = new Vector2(0f, 0.5f);
            leftLive.sizeDelta = new Vector2(140f, 0f);
            var lh = leftLive.gameObject.AddComponent<HorizontalLayoutGroup>();
            lh.spacing = 10;
            lh.childAlignment = TextAnchor.MiddleLeft;
            lh.childControlWidth = false;
            lh.childControlHeight = false;

            var dotRt = UITheme.CreatePanel(leftLive, "Dot", new Color(0.86f, 0.12f, 0.12f, 1f));
            dotRt.sizeDelta = new Vector2(14f, 14f);
            _liveDot = dotRt.GetComponent<Image>();

            var liveTxt = UITheme.CreateText(leftLive, "LiveTxt", "LIVE", 20f, new Color(0.85f, 0.85f, 0.85f, 1f), TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            liveTxt.rectTransform.sizeDelta = new Vector2(90f, 28f);

            // Справа: зелёный глаз + счётчик
            var rightEye = UITheme.CreatePanel(liveRow, "RightEye", new Color(0, 0, 0, 0));
            rightEye.anchorMin = new Vector2(1f, 0f);
            rightEye.anchorMax = new Vector2(1f, 1f);
            rightEye.pivot = new Vector2(1f, 0.5f);
            rightEye.sizeDelta = new Vector2(160f, 0f);
            var rh = rightEye.gameObject.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 10;
            rh.childAlignment = TextAnchor.MiddleRight;
            rh.childControlWidth = false;
            rh.childControlHeight = false;

            var eyeGo = new GameObject("EyeIcon", typeof(RectTransform), typeof(Image));
            eyeGo.transform.SetParent(rightEye, false);
            var eyeImg = eyeGo.GetComponent<Image>();
            eyeImg.sprite = OSIconFactory.GetEyeIcon(new Color(0.14f, 0.78f, 0.24f, 1f));
            eyeImg.preserveAspect = true;
            eyeImg.raycastTarget = false;
            eyeGo.GetComponent<RectTransform>().sizeDelta = new Vector2(26f, 26f);

            _viewersText = UITheme.CreateText(rightEye, "Viewers", _viewerCount.ToString("N0"), 19f, new Color(0.75f, 0.75f, 0.75f, 1f), TextAlignmentOptions.MidlineRight, FontStyles.Normal, monospace: true);
            _viewersText.rectTransform.sizeDelta = new Vector2(84f, 28f);

            // --- ЦЕНТРАЛЬНЫЙ ЗАГОЛОВОК (как WELCOME / TO / THE GAME) ---
            var titleBlock = UITheme.CreatePanel(bg, "TitleBlock", new Color(0, 0, 0, 0));
            titleBlock.anchorMin = new Vector2(0.5f, 0.5f);
            titleBlock.anchorMax = new Vector2(0.5f, 0.5f);
            titleBlock.pivot = new Vector2(0.5f, 0.5f);
            titleBlock.sizeDelta = new Vector2(920f, 180f);
            titleBlock.anchoredPosition = new Vector2(0f, 75f);

            var tv = titleBlock.gameObject.AddComponent<VerticalLayoutGroup>();
            tv.spacing = 4;
            tv.childAlignment = TextAnchor.MiddleCenter;
            tv.childControlWidth = true;
            tv.childControlHeight = true;
            tv.childForceExpandWidth = true;
            tv.childForceExpandHeight = false;

            var topTitle = UITheme.CreateText(titleBlock, "TopTitle", "L.A.R.P. CORP.", 56f, new Color(0.92f, 0.92f, 0.92f, 1f), TextAlignmentOptions.Center, FontStyles.Normal, monospace: true);
            topTitle.characterSpacing = 14f;
            UITheme.AddLayout(topTitle, prefHeight: 68f);

            var midTitle = UITheme.CreateText(titleBlock, "MidTitle", "LOCAL ACCESS", 18f, new Color(0.60f, 0.60f, 0.60f, 1f), TextAlignmentOptions.Center, FontStyles.Normal, monospace: true);
            midTitle.characterSpacing = 12f;
            UITheme.AddLayout(midTitle, prefHeight: 26f);

            var redTitle = UITheme.CreateText(titleBlock, "RedTitle", "ROOT PROTOCOL", 52f, new Color(0.72f, 0.11f, 0.11f, 1f), TextAlignmentOptions.Center, FontStyles.Normal, monospace: true);
            redTitle.characterSpacing = 14f;
            UITheme.AddLayout(redTitle, prefHeight: 64f);

            // --- КНОПКИ ГЛАВНОГО МЕНЮ (чистый текст без рамок, как на 1-м фото) ---
            var menuBox = UITheme.CreatePanel(bg, "MenuButtons", new Color(0, 0, 0, 0));
            menuBox.anchorMin = new Vector2(0.5f, 0.5f);
            menuBox.anchorMax = new Vector2(0.5f, 0.5f);
            menuBox.pivot = new Vector2(0.5f, 0.5f);
            menuBox.sizeDelta = new Vector2(420f, 310f);
            menuBox.anchoredPosition = new Vector2(0f, -185f);
            _mainButtonsGroup = menuBox.gameObject;

            var mv = menuBox.gameObject.AddComponent<VerticalLayoutGroup>();
            mv.spacing = 12;
            mv.childAlignment = TextAnchor.UpperCenter;
            mv.childControlWidth = true;
            mv.childControlHeight = true;
            mv.childForceExpandWidth = true;
            mv.childForceExpandHeight = false;

            CreateTextOnlyMenuButton(menuBox, "NEW GAME", StartNewGame);
            (_continueBtn, _continueLabel) = CreateTextOnlyMenuButton(menuBox, "CONTINUE", ResumeSession);
            CreateTextOnlyMenuButton(menuBox, "OPTIONS", ShowOptions);

            // Отступ перед QUIT как на референсе
            var gap = UITheme.CreatePanel(menuBox, "QuitGap", new Color(0, 0, 0, 0));
            UITheme.AddLayout(gap, prefHeight: 36f);

            CreateTextOnlyMenuButton(menuBox, "QUIT", QuitGame);

            // --- ПАНЕЛЬ НАСТРОЕК (OPTIONS) ---
            BuildOptionsPanel(bg);
        }

        private (Button btn, TextMeshProUGUI label) CreateTextOnlyMenuButton(Transform parent, string text, Action onClick)
        {
            var rt = UITheme.CreatePanel(parent, "MenuBtn_" + text, new Color(0, 0, 0, 0));
            UITheme.AddLayout(rt, prefHeight: 44f);
            var img = rt.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0.001f);

            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var lbl = UITheme.CreateText(rt, "Label", text, 30f, new Color(0.86f, 0.86f, 0.86f, 1f), TextAlignmentOptions.Center, FontStyles.Normal);
            lbl.characterSpacing = 5f;
            UITheme.StretchFill(lbl.rectTransform);

            var hover = rt.gameObject.AddComponent<MenuTextHoverEffect>();
            hover.Setup(btn, lbl);

            btn.onClick.AddListener(() =>
            {
                AudioFeedbackManager.Instance?.PlayClick();
                onClick?.Invoke();
            });

            return (btn, lbl);
        }

        private void BuildOptionsPanel(Transform parent)
        {
            var optRt = UITheme.CreatePanel(parent, "OptionsPanel", new Color(0.04f, 0.04f, 0.05f, 1f));
            optRt.anchorMin = new Vector2(0.5f, 0.5f);
            optRt.anchorMax = new Vector2(0.5f, 0.5f);
            optRt.pivot = new Vector2(0.5f, 0.5f);
            optRt.sizeDelta = new Vector2(560f, 320f);
            optRt.anchoredPosition = new Vector2(0f, -185f);

            var outline = optRt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.22f, 0.24f, 1f);
            outline.effectDistance = new Vector2(1, -1);

            var v = optRt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 16;
            v.padding = new RectOffset(32, 32, 24, 24);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var hdr = UITheme.CreateText(optRt, "Hdr", "OPTIONS", 26f, new Color(0.76f, 0.14f, 0.14f, 1f), TextAlignmentOptions.Center, FontStyles.Normal);
            hdr.characterSpacing = 8f;
            UITheme.AddLayout(hdr, prefHeight: 38f);

            _volumeValueText = AddOptionRow(optRt, "AUDIO VOLUME", "100%", () =>
            {
                _masterVolume = _masterVolume <= 0.05f ? 1f : Mathf.Max(0f, _masterVolume - 0.25f);
                AudioFeedbackManager.Instance?.SetVolume(_masterVolume);
                RefreshOptionsLabels();
            });

            _clockValueText = AddOptionRow(optRt, "CLOCK FORMAT", "12H", () =>
            {
                PlayerState.Instance?.ToggleClockFormat();
                RefreshOptionsLabels();
            });

            _displayValueText = AddOptionRow(optRt, "DISPLAY MODE", Screen.fullScreen ? "FULLSCREEN" : "WINDOWED", () =>
            {
                Screen.fullScreen = !Screen.fullScreen;
                RefreshOptionsLabels();
            });

            var spacer = UITheme.CreatePanel(optRt, "Sp", new Color(0, 0, 0, 0));
            UITheme.AddLayout(spacer, flexHeight: 1f);

            CreateTextOnlyMenuButton(optRt, "BACK", ShowMainButtons);

            _optionsPanel = optRt.gameObject;
            _optionsPanel.SetActive(false);
        }

        private TextMeshProUGUI AddOptionRow(Transform parent, string label, string initialValue, Action onToggle)
        {
            var row = UITheme.CreatePanel(parent, "Opt_" + label, new Color(0, 0, 0, 0));
            UITheme.AddLayout(row, prefHeight: 38f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;

            var lbl = UITheme.CreateText(row, "Lbl", label, 20f, new Color(0.75f, 0.75f, 0.75f, 1f), TextAlignmentOptions.MidlineLeft);
            lbl.characterSpacing = 3f;
            UITheme.AddLayout(lbl, flexWidth: 1f);

            var valBtn = UITheme.CreateButton(row, "ValBtn", initialValue, new Color(0.11f, 0.11f, 0.13f, 1f), UITheme.TextPrimary, 18f, onToggle, monospace: true);
            UITheme.AddLayout(valBtn, prefWidth: 170f);
            return valBtn.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void RefreshOptionsLabels()
        {
            if (_volumeValueText != null)
                _volumeValueText.text = $"{Mathf.RoundToInt(_masterVolume * 100f)}%";
            if (_clockValueText != null && PlayerState.Instance != null)
                _clockValueText.text = PlayerState.Instance.Use24HourFormat ? "24H" : "12H";
            if (_displayValueText != null)
                _displayValueText.text = Screen.fullScreen ? "FULLSCREEN" : "WINDOWED";
        }

        public void ShowMenu(bool initialLaunch)
        {
            if (_rootPanel == null) return;
            _rootPanel.SetActive(true);
            ShowMainButtons();

            if (_continueBtn != null)
                _continueBtn.interactable = _hasActiveSession;
            if (_continueLabel != null)
                _continueLabel.color = _hasActiveSession ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(0.32f, 0.32f, 0.34f, 1f);

            if (GameManager.Instance != null)
                GameManager.Instance.IsModalOpen = true;

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void ShowMainButtons()
        {
            if (_mainButtonsGroup != null) _mainButtonsGroup.SetActive(true);
            if (_optionsPanel != null) _optionsPanel.SetActive(false);
        }

        private void ShowOptions()
        {
            RefreshOptionsLabels();
            if (_mainButtonsGroup != null) _mainButtonsGroup.SetActive(false);
            if (_optionsPanel != null) _optionsPanel.SetActive(true);
        }

        private void StartNewGame()
        {
            _hasActiveSession = true;
            _rootPanel.SetActive(false);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.InitializeNewGame();
                GameManager.Instance.IntroDismissed = true;
                GameManager.Instance.IsModalOpen = false;
            }

            // Запускаем скорректированный Day 1 Game Loop с 08:00 утра!
            Day1StoryController.EnsureInstance().StartDay1FromBeginning();
        }

        private void ResumeSession()
        {
            if (!_hasActiveSession) return;
            _rootPanel.SetActive(false);
            if (GameManager.Instance != null)
                GameManager.Instance.IsModalOpen = false;
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private class MenuTextHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private Button _btn;
            private TextMeshProUGUI _label;

            public void Setup(Button btn, TextMeshProUGUI label)
            {
                _btn = btn;
                _label = label;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (_btn == null || !_btn.interactable || _label == null) return;
                _label.color = new Color(0.92f, 0.24f, 0.20f, 1f);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (_btn == null || _label == null) return;
                _label.color = _btn.interactable ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(0.32f, 0.32f, 0.34f, 1f);
            }
        }
    }
}
