using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;
using Project.LinuxOS;

namespace Project.UI
{
    /// <summary>
    /// Экранный HUD квартиры (Screen Space Overlay):
    /// - Статус-карта игрока в комнате (День, Время из PlayerState, статус ПК, $, Усталость, Trace, 3 шкалы)
    /// - Контекстные подсказки взаимодействия (Дверь, Шкаф, Энергетик, Плейсмент роутера, Компьютер, Диван)
    /// - Сюжетные и диалоговые слайды (Офис 1-го дня, Озарение, диалоги с Мией и Маркусом)
    /// - Модальные окна «Итоги дня» и «Game Over»
    /// </summary>
    public class ApartmentHudController : MonoBehaviour
    {
        public static ApartmentHudController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private GameObject _roomHudGroup;
        private TextMeshProUGUI _statsText;
        private TextMeshProUGUI _objectiveText;
        private TextMeshProUGUI _parseLiveText;
        private TextMeshProUGUI _promptText;
        private GameObject _promptBox;
        private TextMeshProUGUI _roomToastText;
        private float _roomToastClearAt;

        // Сюжетно-диалоговая модалка (Офис 1-го дня + диалоги с Мией и Маркусом)
        private GameObject _storyModal;
        private TextMeshProUGUI _storySpeakerText;
        private TextMeshProUGUI _storyTimeText;
        private TextMeshProUGUI _storyTitleText;
        private TextMeshProUGUI _storyBodyText;
        private TextMeshProUGUI _storyBtnLabel;
        private StorySlideData[] _activeSlides;
        private int _activeSlideIndex;
        private Action _onStorySequenceFinished;

        // Модалка «Итоги дня»
        private GameObject _daySummaryModal;
        private TextMeshProUGUI _summaryTitle;
        private TextMeshProUGUI _summaryFinanceText;
        private TextMeshProUGUI _summaryNewsText;
        private TextMeshProUGUI _summaryScalesText;

        // Модалка «Game Over»
        private GameObject _gameOverModal;
        private TextMeshProUGUI _gameOverReasonText;

        public static ApartmentHudController EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<ApartmentHudController>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }
            var go = new GameObject("ApartmentHudCanvas");
            return go.AddComponent<ApartmentHudController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildCanvas();
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged += RefreshStats;
                GameManager.Instance.OnToast += ShowRoomToast;
                GameManager.Instance.OnDaySummaryReady += ShowDaySummary;
                GameManager.Instance.OnGameOverTriggered += ShowGameOver;
            }

            if (PlayerState.Instance != null)
            {
                PlayerState.Instance.OnTimeChanged += RefreshStats;
                PlayerState.Instance.OnComputerStateChanged += RefreshStats;
            }

            if (Day1StoryController.Instance != null)
            {
                Day1StoryController.Instance.OnStageChanged += HandleStageChanged;
                Day1StoryController.Instance.OnStorySequenceRequested += ShowStorySequence;
            }

            RefreshStats();
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStateChanged -= RefreshStats;
                GameManager.Instance.OnToast -= ShowRoomToast;
                GameManager.Instance.OnDaySummaryReady -= ShowDaySummary;
                GameManager.Instance.OnGameOverTriggered -= ShowGameOver;
            }

            if (PlayerState.Instance != null)
            {
                PlayerState.Instance.OnTimeChanged -= RefreshStats;
                PlayerState.Instance.OnComputerStateChanged -= RefreshStats;
            }

            if (Day1StoryController.Instance != null)
            {
                Day1StoryController.Instance.OnStageChanged -= HandleStageChanged;
                Day1StoryController.Instance.OnStorySequenceRequested -= ShowStorySequence;
            }
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            bool atPC = PlayerStateController.Instance != null &&
                        (PlayerStateController.Instance.IsAtComputer || PlayerStateController.Instance.IsFlying);
            bool menuOpen = MainMenuController.Instance != null && MainMenuController.Instance.IsOpen;

            if (_roomHudGroup != null)
                _roomHudGroup.SetActive(!atPC && !gm.IsModalOpen && !menuOpen);

            if (!atPC && !gm.IsModalOpen && !menuOpen)
            {
                UpdateLiveParseIndicator();
                UpdateInteractionPrompt();
            }

            if (_roomToastText != null && !string.IsNullOrEmpty(_roomToastText.text) && Time.unscaledTime > _roomToastClearAt)
            {
                _roomToastText.text = string.Empty;
            }
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            var rootRt = GetComponent<RectTransform>();

            // === 1. ROOM HUD GROUP ===
            var hudRt = UITheme.CreatePanel(rootRt, "RoomHudGroup", new Color(0, 0, 0, 0));
            UITheme.StretchFill(hudRt);
            hudRt.GetComponent<Image>().raycastTarget = false;
            _roomHudGroup = hudRt.gameObject;

            // Верхняя левая панель ресурсов
            var statsCard = UITheme.CreatePanel(hudRt, "StatsCard", new Color(0.08f, 0.09f, 0.11f, 0.92f));
            statsCard.anchorMin = new Vector2(0f, 1f);
            statsCard.anchorMax = new Vector2(0f, 1f);
            statsCard.pivot = new Vector2(0f, 1f);
            statsCard.anchoredPosition = new Vector2(20, -20);
            statsCard.sizeDelta = new Vector2(475, 168);

            var statsOutline = statsCard.gameObject.AddComponent<Outline>();
            statsOutline.effectColor = new Color(0.24f, 0.26f, 0.30f, 0.9f);
            statsOutline.effectDistance = new Vector2(1, -1);

            var sv = statsCard.gameObject.AddComponent<VerticalLayoutGroup>();
            sv.padding = new RectOffset(16, 16, 12, 12);
            sv.spacing = 6;
            sv.childControlWidth = true;
            sv.childControlHeight = true;

            var title = UITheme.CreateText(statsCard, "Title", "L.A.R.P. CORP.  //  УБЕЖИЩЕ", 18, UITheme.WindowOrange, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(title, prefHeight: 26);

            _statsText = UITheme.CreateText(statsCard, "Stats", "", 16, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
            UITheme.AddLayout(_statsText, prefHeight: 84);

            _parseLiveText = UITheme.CreateText(statsCard, "ParseLive", "", 14, UITheme.AccentAmber, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, monospace: true);
            UITheme.AddLayout(_parseLiveText, prefHeight: 22);

            // Верхний центр: текущая цель
            var objCard = UITheme.CreatePanel(hudRt, "ObjectiveCard", new Color(0.08f, 0.09f, 0.11f, 0.90f));
            objCard.anchorMin = new Vector2(0.5f, 1f);
            objCard.anchorMax = new Vector2(0.5f, 1f);
            objCard.pivot = new Vector2(0.5f, 1f);
            objCard.anchoredPosition = new Vector2(115, -20);
            objCard.sizeDelta = new Vector2(880, 68);
            var objOutline = objCard.gameObject.AddComponent<Outline>();
            objOutline.effectColor = UITheme.WindowOrange;
            objOutline.effectDistance = new Vector2(1, -1);

            _objectiveText = UITheme.CreateText(objCard, "ObjTxt", "", 16, UITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.StretchFill(_objectiveText.rectTransform, 14, 8, 14, 8);

            // Правый верх: быстрые кнопки
            var quickCol = UITheme.CreatePanel(hudRt, "QuickActions", new Color(0, 0, 0, 0));
            quickCol.anchorMin = new Vector2(1f, 1f);
            quickCol.anchorMax = new Vector2(1f, 1f);
            quickCol.pivot = new Vector2(1f, 1f);
            quickCol.anchoredPosition = new Vector2(-20, -20);
            quickCol.sizeDelta = new Vector2(220, 112);
            quickCol.GetComponent<Image>().raycastTarget = false;
            var qv = quickCol.gameObject.AddComponent<VerticalLayoutGroup>();
            qv.spacing = 8;
            qv.childControlWidth = true;
            qv.childControlHeight = true;
            qv.childForceExpandWidth = true;
            qv.childForceExpandHeight = true;

            UITheme.CreateButton(quickCol, "GoPCBtn", "К компьютеру", UITheme.WindowOrange, UITheme.TextPrimary, 16, () =>
            {
                if (MonitorInteractable.ActiveInstance != null)
                    MonitorInteractable.ActiveInstance.InteractWithMonitor();
                else
                    PlayerStateController.Instance?.EnterComputer();
            });

            UITheme.CreateButton(quickCol, "SleepBtn", "Лечь спать (Итоги)", UITheme.BgCard, UITheme.TextPrimary, 15, () =>
            {
                GameManager.Instance?.SleepAndEndDay();
            });

            // Низ-центр: контекстная подсказка взаимодействия + управление
            var promptPanel = UITheme.CreatePanel(hudRt, "PromptPanel", new Color(0.08f, 0.09f, 0.11f, 0.94f));
            promptPanel.anchorMin = new Vector2(0.5f, 0f);
            promptPanel.anchorMax = new Vector2(0.5f, 0f);
            promptPanel.pivot = new Vector2(0.5f, 0f);
            promptPanel.anchoredPosition = new Vector2(0, 72);
            promptPanel.sizeDelta = new Vector2(620, 52);
            var prOutline = promptPanel.gameObject.AddComponent<Outline>();
            prOutline.effectColor = UITheme.WindowOrange;
            prOutline.effectDistance = new Vector2(1, -1);

            _promptText = UITheme.CreateText(promptPanel, "PromptTxt", "", 18, UITheme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.StretchFill(_promptText.rectTransform, 12, 6, 12, 6);
            _promptBox = promptPanel.gameObject;
            _promptBox.SetActive(false);

            var controlsBar = UITheme.CreatePanel(hudRt, "ControlsBar", new Color(0.08f, 0.09f, 0.11f, 0.84f));
            controlsBar.anchorMin = new Vector2(0.5f, 0f);
            controlsBar.anchorMax = new Vector2(0.5f, 0f);
            controlsBar.pivot = new Vector2(0.5f, 0f);
            controlsBar.anchoredPosition = new Vector2(0, 16);
            controlsBar.sizeDelta = new Vector2(940, 38);
            var ctrlTxt = UITheme.CreateText(controlsBar, "CtrlTxt",
                "ПКМ — передвижение   |   ЛКМ / E — взаимодействие (дверь, шкаф, энергетик, ПК, диван)   |   ESC — меню",
                14, UITheme.TextMuted, TextAlignmentOptions.Center);
            UITheme.StretchFill(ctrlTxt.rectTransform, 10, 4, 10, 4);

            _roomToastText = UITheme.CreateText(hudRt, "RoomToast", "", 17, UITheme.AccentAmber, TextAlignmentOptions.Center, FontStyles.Bold);
            _roomToastText.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _roomToastText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _roomToastText.rectTransform.pivot = new Vector2(0.5f, 0f);
            _roomToastText.rectTransform.anchoredPosition = new Vector2(0, 134);
            _roomToastText.rectTransform.sizeDelta = new Vector2(1020, 34);

            // === 2. STORY & DIALOGUE MODAL ===
            BuildStoryModal(rootRt);

            // === 3. END OF DAY SUMMARY MODAL ===
            BuildDaySummaryModal(rootRt);

            // === 4. GAME OVER MODAL ===
            BuildGameOverModal(rootRt);
        }

        private void BuildStoryModal(RectTransform rootRt)
        {
            var dim = UITheme.CreatePanel(rootRt, "StoryModal", new Color(0.02f, 0.02f, 0.03f, 0.90f));
            UITheme.StretchFill(dim);
            _storyModal = dim.gameObject;
            _storyModal.SetActive(false);

            var card = UITheme.CreatePanel(dim, "StoryCard", new Color(0.09f, 0.10f, 0.12f, 1f));
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(820, 440);

            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = UITheme.WindowOrange;
            outline.effectDistance = new Vector2(1, -1);

            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14;
            v.padding = new RectOffset(32, 32, 26, 26);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var topRow = UITheme.CreatePanel(card, "TopRow", new Color(0, 0, 0, 0));
            UITheme.AddLayout(topRow, prefHeight: 28);
            var th = topRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandHeight = true;
            th.childForceExpandWidth = false;

            _storySpeakerText = UITheme.CreateText(topRow, "Speaker", "", 16, UITheme.WindowOrange, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, monospace: true);
            UITheme.AddLayout(_storySpeakerText, flexWidth: 1);

            _storyTimeText = UITheme.CreateText(topRow, "Time", "", 16, UITheme.TextMuted, TextAlignmentOptions.MidlineRight, FontStyles.Bold, monospace: true);
            UITheme.AddLayout(_storyTimeText, prefWidth: 120);

            _storyTitleText = UITheme.CreateText(card, "Title", "", 26, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(_storyTitleText, prefHeight: 38);

            var sep = UITheme.CreatePanel(card, "Sep", new Color(1, 1, 1, 0.12f));
            UITheme.AddLayout(sep, prefHeight: 1);

            _storyBodyText = UITheme.CreateText(card, "Body", "", 19, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
            UITheme.AddLayout(_storyBodyText, flexHeight: 1);

            var nextBtn = UITheme.CreateButton(card, "NextBtn", "ДАЛЕЕ", UITheme.WindowOrange, UITheme.TextPrimary, 18, AdvanceStorySlide);
            UITheme.AddLayout(nextBtn, prefHeight: 54);
            _storyBtnLabel = nextBtn.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void BuildDaySummaryModal(RectTransform rootRt)
        {
            var dim = UITheme.CreatePanel(rootRt, "DaySummaryModal", UITheme.BgModalDim);
            UITheme.StretchFill(dim);
            _daySummaryModal = dim.gameObject;
            _daySummaryModal.SetActive(false);

            var card = UITheme.CreatePanel(dim, "SummaryCard", UITheme.BgPanel);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(880, 620);

            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14;
            v.padding = new RectOffset(32, 32, 26, 26);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            _summaryTitle = UITheme.CreateText(card, "Title", "ИТОГИ ДНЯ 1", 30, UITheme.WindowOrange, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.AddLayout(_summaryTitle, prefHeight: 42);

            var finBox = UITheme.CreatePanel(card, "FinBox", UITheme.BgDesktop);
            UITheme.AddLayout(finBox, prefHeight: 185);
            _summaryFinanceText = UITheme.CreateText(finBox, "FinTxt", "", 19, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
            UITheme.StretchFill(_summaryFinanceText.rectTransform, 18, 14, 18, 14);

            var newsBox = UITheme.CreatePanel(card, "NewsBox", UITheme.BgCard);
            UITheme.AddLayout(newsBox, prefHeight: 145);
            _summaryNewsText = UITheme.CreateText(newsBox, "NewsTxt", "", 18, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
            UITheme.StretchFill(_summaryNewsText.rectTransform, 18, 14, 18, 14);

            _summaryScalesText = UITheme.CreateText(card, "ScalesTxt", "", 19, UITheme.AccentAmber, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.AddLayout(_summaryScalesText, prefHeight: 40);

            var nextDayBtn = UITheme.CreateButton(card, "NextDayBtn", "ПРОСНУТЬСЯ И НАЧАТЬ СЛЕДУЮЩИЙ ДЕНЬ ->", UITheme.AccentGreen, UITheme.TextPrimary, 20, () =>
            {
                _daySummaryModal.SetActive(false);
                GameManager.Instance?.StartNextDay();
            });
            UITheme.AddLayout(nextDayBtn, prefHeight: 60);
        }

        private void BuildGameOverModal(RectTransform rootRt)
        {
            var dim = UITheme.CreatePanel(rootRt, "GameOverModal", new Color(0.08f, 0.01f, 0.02f, 0.94f));
            UITheme.StretchFill(dim);
            _gameOverModal = dim.gameObject;
            _gameOverModal.SetActive(false);

            var card = UITheme.CreatePanel(dim, "GOCard", new Color(0.08f, 0.09f, 0.10f, 1f));
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(780, 420);

            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = UITheme.AccentRed;
            outline.effectDistance = new Vector2(1, -1);

            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 18;
            v.padding = new RectOffset(32, 32, 28, 28);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var title = UITheme.CreateText(card, "Title", "КОНЕЦ ИГРЫ — ПРОВАЛ", 32, UITheme.AccentRed, TextAlignmentOptions.Center, FontStyles.Bold);
            UITheme.AddLayout(title, prefHeight: 48);

            _gameOverReasonText = UITheme.CreateText(card, "Reason", "", 20, UITheme.TextPrimary, TextAlignmentOptions.Center);
            UITheme.AddLayout(_gameOverReasonText, flexHeight: 1);

            var retryBtn = UITheme.CreateButton(card, "RetryBtn", "НАЧАТЬ ДЕНЬ 1 ЗАНОВО", UITheme.WindowOrange, UITheme.TextPrimary, 20, () =>
            {
                _gameOverModal.SetActive(false);
                GameManager.Instance?.InitializeNewGame();
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.IntroDismissed = true;
                    GameManager.Instance.IsModalOpen = false;
                }
                Day1StoryController.Instance?.StartDay1FromBeginning();
            });
            UITheme.AddLayout(retryBtn, prefHeight: 58);
        }

        private void ShowStorySequence(StorySlideData[] slides, Action onFinished)
        {
            if (slides == null || slides.Length == 0)
            {
                onFinished?.Invoke();
                return;
            }

            _activeSlides = slides;
            _activeSlideIndex = 0;
            _onStorySequenceFinished = onFinished;

            if (GameManager.Instance != null)
                GameManager.Instance.IsModalOpen = true;

            _storyModal.SetActive(true);
            RenderActiveStorySlide();
        }

        private void RenderActiveStorySlide()
        {
            if (_activeSlides == null || _activeSlideIndex >= _activeSlides.Length)
                return;

            var s = _activeSlides[_activeSlideIndex];
            _storySpeakerText.text = s.Speaker;
            _storyTimeText.text = s.TimeBadge;
            _storyTitleText.text = s.Title;
            _storyBodyText.text = s.Body;
            _storyBtnLabel.text = string.IsNullOrEmpty(s.ButtonLabel) ? "ДАЛЕЕ" : s.ButtonLabel;
        }

        private void AdvanceStorySlide()
        {
            _activeSlideIndex++;
            if (_activeSlides != null && _activeSlideIndex < _activeSlides.Length)
            {
                RenderActiveStorySlide();
                return;
            }

            _storyModal.SetActive(false);
            if (GameManager.Instance != null)
                GameManager.Instance.IsModalOpen = false;

            Action cb = _onStorySequenceFinished;
            _onStorySequenceFinished = null;
            _activeSlides = null;
            cb?.Invoke();
        }

        private void HandleStageChanged(Day1Stage stage)
        {
            RefreshStats();
        }

        private void RefreshStats()
        {
            var gm = GameManager.Instance;
            if (gm == null || _statsText == null) return;

            string traceCol = gm.Trace >= 65 ? "#D14343" : (gm.Trace >= 30 ? "#E09E33" : "#3DB861");
            string fatCol = gm.Fatigue >= 80 ? "#D14343" : (gm.Fatigue >= 45 ? "#E09E33" : "#D9682A");
            string timeStr = PlayerState.Instance != null ? PlayerState.Instance.GetFormattedTime() : "8:00 AM";
            string pcStr = (PlayerState.Instance != null && PlayerState.Instance.IsComputerPoweredOn) ? "ПК: ВКЛ" : "ПК: ВЫКЛ";

            _statsText.text =
                $"День {gm.Day}  •  <b>{timeStr}</b>  •  {pcStr}  •  <color=#3DB861><b>${gm.Money}</b></color>\n" +
                $"Усталость: <color={fatCol}><b>{gm.Fatigue}%</b></color>   |   Trace: <color={traceCol}><b>{gm.Trace}%</b></color>   |   Дата-Центр: Ур.{gm.DataCenterLevel}\n" +
                $"Авторитет: {gm.Authority}  •  Фолловеры: {gm.Followers}  •  Досье: {gm.Dossier}";

            string obj = Day1StoryController.Instance != null
                ? Day1StoryController.Instance.GetStageObjectiveText()
                : gm.GetCurrentObjectiveHint();

            _objectiveText.text = "ЗАДАЧА: " + obj;
        }

        private void UpdateLiveParseIndicator()
        {
            var gm = GameManager.Instance;
            if (gm == null || _parseLiveText == null) return;

            var active = gm.ParseJobs.FirstOrDefault(p => !p.Completed);
            if (active != null)
            {
                _parseLiveText.color = UITheme.AccentAmber;
                _parseLiveText.text = $"Дата-Центр парсит архив: {Mathf.RoundToInt(active.Progress * 100f)}%...";
            }
            else if (gm.GetReadyEvidenceCount() > 0)
            {
                _parseLiveText.color = UITheme.AccentGreen;
                _parseLiveText.text = $"[OK] В Дата-Центре готов компромат ({gm.GetReadyEvidenceCount()}) — подойдите к ПК!";
            }
            else
            {
                _parseLiveText.text = string.Empty;
            }
        }

        private void UpdateInteractionPrompt()
        {
            if (_promptBox == null) return;

            if (RouterPlacementController.Instance != null && RouterPlacementController.Instance.IsPlacing)
            {
                _promptBox.SetActive(true);
                _promptText.text = "[ЛКМ] Поставить роутер на поверхность   |   [R] Повернуть";
                return;
            }

            // Проверяем сюжетные объекты в квартире (Дверь, Шкаф, Энергетик)
            foreach (var prop in RoomPropInteractable.All)
            {
                if (prop != null && prop.IsInteractionEnabled && (prop.IsHovered || prop.IsPlayerNear))
                {
                    _promptBox.SetActive(true);
                    _promptText.text = prop.IsPlayerNear
                        ? $"[ЛКМ / E] {prop.PromptLabel}"
                        : $"[ЛКМ] Подойти: {prop.PromptLabel}";
                    return;
                }
            }

            var monitor = MonitorInteractable.ActiveInstance;
            if (monitor != null && (monitor.IsHovered || monitor.IsPlayerNear))
            {
                _promptBox.SetActive(true);
                _promptText.text = monitor.IsPlayerNear
                    ? "[ЛКМ / E] Сесть за компьютер"
                    : "[ЛКМ] Подойти и сесть за компьютер";
                return;
            }

            var sofa = SleepInteractable.ActiveInstance;
            if (sofa != null && (sofa.IsHovered || sofa.IsPlayerNear))
            {
                _promptBox.SetActive(true);
                _promptText.text = sofa.IsPlayerNear
                    ? "[ЛКМ / E] Лечь спать (Завершить день)"
                    : "[ЛКМ] Подойти к дивану и лечь спать";
                return;
            }

            _promptBox.SetActive(false);
        }

        private void ShowRoomToast(string message)
        {
            if (_roomToastText != null)
            {
                _roomToastText.text = message;
                _roomToastClearAt = Time.unscaledTime + 5.5f;
            }
        }

        private void ShowDaySummary(DaySummaryReport report)
        {
            if (_daySummaryModal == null || report == null) return;

            _summaryTitle.text = $"ИТОГИ ДНЯ {report.CompletedDay}";

            string sleepLine = report.Overslept
                ? $"<color=#D14343>• Штраф за недосып (проспали утреннюю смену!): -${report.OversleepPenalty}</color>"
                : "<color=#3DB861>• Режим сна в норме (утренняя смена без штрафов): $0</color>";

            _summaryFinanceText.text =
                $"<b>ПОДСЧЁТ ФИНАНСОВ:</b>\n" +
                $"• Зарплата младшего клерка: <color=#3DB861>+${report.BaseSalary}</color>\n" +
                $"• Заработок за день (тикеты, фриланс, блог/босс): <color=#3DB861>+${report.DayEarnedExtra}</color>\n" +
                $"• Аренда убежища и счета за электричество: <color=#E09E33>-${report.RentAndBills}</color>\n" +
                $"{sleepLine}\n" +
                $"<b>Итоговый баланс: <color=#3DB861>${report.ResultingMoney}</color></b>";

            _summaryNewsText.text =
                $"<b>СВОДКА НОВОСТЕЙ:</b>\n" +
                $"<color=#D9682A>{report.NewsHeadline}</color>\n" +
                $"{report.NewsBody}";

            _summaryScalesText.text =
                $"Авторитет: {report.Authority}   |   Фолловеры: {report.Followers}   |   Досье: {report.Dossier}   |   Trace: {report.Trace}%";

            _daySummaryModal.SetActive(true);
        }

        private void ShowGameOver(string reason)
        {
            if (_gameOverModal == null) return;
            _daySummaryModal?.SetActive(false);
            _storyModal?.SetActive(false);
            _gameOverReasonText.text = reason;
            _gameOverModal.SetActive(true);
        }
    }
}
