using System;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Браузер (Intranet)» — Шаг 1 Core Loop:
    /// инспекция служебных HTML-страниц, кликабельные скрытые блоки, просмотр исходного кода и поиск по тексту.
    /// </summary>
    public class BrowserAppView : MonoBehaviour
    {
        private RectTransform _bookmarksContent;
        private TextMeshProUGUI _urlText;
        private TextMeshProUGUI _pageTitleText;
        private TextMeshProUGUI _pageBodyText;
        private TMP_InputField _searchInput;
        private Button _toggleSourceBtn;
        private TextMeshProUGUI _toggleSourceLabel;
        private RectTransform _interactiveArea;
        private TextMeshProUGUI _searchStatusText;

        private string _currentUrl;
        private bool _viewSourceMode;
        private string _searchQuery = string.Empty;
        private LarpDesktopOS _desktop;

        public void Initialize(LarpDesktopOS desktop, RectTransform root)
        {
            _desktop = desktop;

            var hlg = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14;
            hlg.padding = new RectOffset(14, 14, 14, 14);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;

            // Левая панель: закладки служебных страниц
            var leftPanel = UITheme.CreatePanel(root, "BookmarksPanel", UITheme.BgPanel);
            UITheme.AddLayout(leftPanel, prefWidth: 310);
            var lv = leftPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 8;
            lv.padding = new RectOffset(12, 12, 12, 12);
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;

            var bmTitle = UITheme.CreateText(leftPanel, "Title", "СЛУЖЕБНЫЕ СТРАНИЦЫ", 20, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(bmTitle, prefHeight: 34);

            var (scroll, content) = UITheme.CreateVerticalScrollView(leftPanel, "PagesScroll", UITheme.BgDesktop, 8, 8);
            UITheme.AddLayout(scroll, flexHeight: 1);
            _bookmarksContent = content;

            // Правая панель: браузер + тулбар поиска + исходный код
            var rightPanel = UITheme.CreatePanel(root, "BrowserMain", UITheme.BgPanel);
            UITheme.AddLayout(rightPanel, flexWidth: 1);
            var rv = rightPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 10;
            rv.padding = new RectOffset(16, 16, 14, 14);
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;

            // Адресная строка + кнопка Исходный код
            var addressRow = UITheme.CreatePanel(rightPanel, "AddressRow", UITheme.BgDesktop);
            UITheme.AddLayout(addressRow, prefHeight: 50);
            var addrH = addressRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            addrH.spacing = 10;
            addrH.padding = new RectOffset(12, 8, 6, 6);
            addrH.childControlWidth = true;
            addrH.childControlHeight = true;
            addrH.childForceExpandHeight = true;
            addrH.childForceExpandWidth = false;

            _urlText = UITheme.CreateText(addressRow, "Url", "https://intranet.larp.corp/", 18, UITheme.AccentGreen);
            UITheme.AddLayout(_urlText, flexWidth: 1);

            _toggleSourceBtn = UITheme.CreateButton(
                addressRow,
                "SourceBtn",
                "</> Исходный код",
                UITheme.WindowOrange,
                UITheme.TextPrimary,
                16,
                () =>
                {
                    _viewSourceMode = !_viewSourceMode;
                    Refresh();
                });
            UITheme.AddLayout(_toggleSourceBtn, prefWidth: 185);
            _toggleSourceLabel = _toggleSourceBtn.GetComponentInChildren<TextMeshProUGUI>();

            var popoutSrcBtn = UITheme.CreateButton(
                addressRow,
                "PopoutSourceBtn",
                "Source Viewer",
                UITheme.BgCard,
                UITheme.TextPrimary,
                15,
                () =>
                {
                    var p = GameManager.Instance?.Pages.FirstOrDefault(x => x.Url == _currentUrl);
                    if (p != null)
                        _desktop?.OpenTextViewer("Source Viewer", p.Url, p.HtmlSource);
                });
            UITheme.AddLayout(popoutSrcBtn, prefWidth: 135);

            // Строка поиска по тексту (вспомогательная механика со слайда 18!)
            var searchRow = UITheme.CreatePanel(rightPanel, "SearchRow", UITheme.BgCard);
            UITheme.AddLayout(searchRow, prefHeight: 46);
            var sh = searchRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            sh.spacing = 8;
            sh.padding = new RectOffset(10, 10, 5, 5);
            sh.childControlWidth = true;
            sh.childControlHeight = true;
            sh.childForceExpandHeight = true;
            sh.childForceExpandWidth = false;

            var sLbl = UITheme.CreateText(searchRow, "SLbl", "Поиск (Ctrl+F):", 16, UITheme.TextMuted);
            UITheme.AddLayout(sLbl, prefWidth: 150);

            _searchInput = UITheme.CreateInputField(searchRow, "SearchInput", "Введите KEY, BASE64, HEX...", 17);
            UITheme.AddLayout(_searchInput, flexWidth: 1);
            _searchInput.onValueChanged.AddListener(val =>
            {
                _searchQuery = val ?? string.Empty;
                RefreshPageContent();
            });

            var quickKeyBtn = UITheme.CreateButton(searchRow, "QKey", "KEY", UITheme.BgInput, UITheme.AccentCyan, 15, () =>
            {
                _searchInput.text = "KEY";
                _viewSourceMode = true;
                Refresh();
            });
            UITheme.AddLayout(quickKeyBtn, prefWidth: 74);

            var clearBtn = UITheme.CreateButton(searchRow, "QClear", "Сброс", UITheme.BgInput, UITheme.TextMuted, 15, () =>
            {
                _searchInput.text = string.Empty;
                RefreshPageContent();
            });
            UITheme.AddLayout(clearBtn, prefWidth: 84);

            _searchStatusText = UITheme.CreateText(rightPanel, "SearchStatus", "", 15, UITheme.AccentAmber);
            UITheme.AddLayout(_searchStatusText, prefHeight: 22);

            // Контент страницы
            var contentCard = UITheme.CreatePanel(rightPanel, "PageContentCard", UITheme.BgDesktop);
            UITheme.AddLayout(contentCard, flexHeight: 1);
            var cv = contentCard.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.spacing = 12;
            cv.padding = new RectOffset(18, 18, 16, 16);
            cv.childControlWidth = true;
            cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;

            _pageTitleText = UITheme.CreateText(contentCard, "PageTitle", "", 24, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(_pageTitleText, prefHeight: 38);

            _pageBodyText = UITheme.CreateText(contentCard, "PageBody", "", 19, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
            UITheme.AddLayout(_pageBodyText, flexHeight: 1);

            // Нижняя интерактивная зона (раскрытие скрытых элементов / извлечение ключа)
            _interactiveArea = UITheme.CreatePanel(rightPanel, "InteractiveArea", UITheme.BgCard);
            UITheme.AddLayout(_interactiveArea, prefHeight: 86);
            var iaH = _interactiveArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            iaH.spacing = 14;
            iaH.padding = new RectOffset(14, 14, 12, 12);
            iaH.childControlWidth = true;
            iaH.childControlHeight = true;
            iaH.childForceExpandHeight = true;
            iaH.childForceExpandWidth = false;

            Refresh();
        }

        public void OpenUrl(string url)
        {
            _currentUrl = url;
            _viewSourceMode = false;
            Refresh();
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            if (gm == null || _bookmarksContent == null) return;

            var pages = gm.Pages.Where(p => p.UnlockDay <= gm.Day).ToList();
            if (string.IsNullOrEmpty(_currentUrl) || pages.All(p => p.Url != _currentUrl))
            {
                _currentUrl = pages.FirstOrDefault()?.Url;
            }

            UITheme.ClearChildren(_bookmarksContent);
            foreach (var page in pages)
            {
                var frag = gm.Fragments.FirstOrDefault(f => f.Id == page.FragmentId);
                bool found = frag != null && frag.IsFound;
                bool selected = page.Url == _currentUrl;

                var card = UITheme.CreatePanel(_bookmarksContent, "Page_" + page.Url, selected ? UITheme.BgCardHover : UITheme.BgCard);
                UITheme.AddLayout(card, prefHeight: 74);

                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card.GetComponent<Image>();
                string u = page.Url;
                btn.onClick.AddListener(() =>
                {
                    AudioFeedbackManager.Instance?.PlayClick();
                    _currentUrl = u;
                    _viewSourceMode = false;
                    Refresh();
                });

                var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
                v.padding = new RectOffset(12, 12, 8, 8);
                v.spacing = 3;
                v.childControlWidth = true;
                v.childControlHeight = true;

                string statusTag = found ? "<color=#2EE676>[КЛЮЧ НАЙДЕН]</color>" : "<color=#FFB84D>[ОСМОТРЕТЬ]</color>";
                var t1 = UITheme.CreateText(card, "T1", $"{statusTag} {page.Title}", 15, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(t1, prefHeight: 28);

                var t2 = UITheme.CreateText(card, "T2", page.Url, 14, UITheme.TextMuted);
                UITheme.AddLayout(t2, prefHeight: 22);
            }

            RefreshPageContent();
        }

        private void RefreshPageContent()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            var page = gm.Pages.FirstOrDefault(p => p.Url == _currentUrl);
            if (page == null) return;

            if (PlayerState.Instance != null && !PlayerState.Instance.IsWifiConnected)
            {
                _urlText.text = $"https://{page.Url}  [OFFLINE]";
                _pageTitleText.text = "Нет подключения к интернету";
                _pageBodyText.text =
                    "Не удалось загрузить страницу корпоративного интранета.\n\n" +
                    "Откройте значок <b>Wi-Fi</b> в правом верхнем углу таскбара и подключитесь к доступной открытой сети без пароля.";
                _searchStatusText.text = "Wi-Fi отключён — сеть недоступна.";
                UITheme.ClearChildren(_interactiveArea);
                var offlineTxt = UITheme.CreateText(_interactiveArea, "OfflineHint", "Подключитесь к Wi-Fi в правом верхнем углу панели задач.", 16, UITheme.AccentAmber);
                UITheme.AddLayout(offlineTxt, flexWidth: 1);
                return;
            }

            var frag = gm.Fragments.FirstOrDefault(f => f.Id == page.FragmentId);

            _urlText.text = $"https://{page.Url}" + (_viewSourceMode ? "  [VIEW-SOURCE]" : "");
            _toggleSourceLabel.text = _viewSourceMode ? "← Обычный вид" : "</> Исходный код";

            string rawDisplay = _viewSourceMode
                ? EscapeHtmlForRichText(page.HtmlSource)
                : $"{page.Subtitle}\n\n{page.VisibleBody}" +
                  (page.InteractiveRevealed ? $"\n\n<color=#2EE676>> {page.InteractiveBlockRevealText}</color>" : "");

            int matches = 0;
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                string escapedQuery = Regex.Escape(_searchQuery.Trim());
                rawDisplay = Regex.Replace(
                    rawDisplay,
                    escapedQuery,
                    m =>
                    {
                        matches++;
                        return $"<mark=#FFB84D55><color=#FFB84D><b>{m.Value}</b></color></mark>";
                    },
                    RegexOptions.IgnoreCase);
                _searchStatusText.text = matches > 0
                    ? $"Найдено совпадений по «{_searchQuery}»: {matches}"
                    : $"По «{_searchQuery}» ничего не найдено. Попробуйте переключить «Исходный код»!";
            }
            else
            {
                _searchStatusText.text = _viewSourceMode
                    ? "Режим инспекции HTML-кода активен. Ищите скрытые комментарии <!-- KEY_PART ... -->"
                    : "Осмотрите страницу или откройте «Исходный код» для поиска скрытых фрагментов ключа.";
            }

            _pageTitleText.text = _viewSourceMode ? $"Исходный код: {page.Url}" : page.Title;
            _pageBodyText.text = rawDisplay;

            // Обновляем нижнюю панель действий
            UITheme.ClearChildren(_interactiveArea);

            if (frag == null)
                return;

            if (frag.IsFound)
            {
                var doneTxt = UITheme.CreateText(
                    _interactiveArea,
                    "DoneTxt",
                    $"<color=#2EE676>[OK] Фрагмент #{frag.SlotIndex} ({frag.RawValue}) уже добавлен в буфер ключей!</color>",
                    18,
                    UITheme.AccentGreen);
                UITheme.AddLayout(doneTxt, flexWidth: 1);

                var goDecoder = UITheme.CreateButton(
                    _interactiveArea,
                    "GoDecoderBtn",
                    "Открыть Декодер →",
                    UITheme.AccentCyan,
                    UITheme.TextDark,
                    17,
                    () => _desktop.SwitchTab(LarpDesktopOS.AppTab.Decoder));
                UITheme.AddLayout(goDecoder, prefWidth: 250);
                return;
            }

            if (page.HasInteractiveHiddenBlock && !page.InteractiveRevealed && !_viewSourceMode)
            {
                var revealBtn = UITheme.CreateButton(
                    _interactiveArea,
                    "RevealBtn",
                    page.InteractiveBlockLabel,
                    UITheme.AccentAmber,
                    UITheme.TextDark,
                    18,
                    () =>
                    {
                        page.InteractiveRevealed = true;
                        gm.DiscoverFragment(frag.Id);
                        Refresh();
                    });
                UITheme.AddLayout(revealBtn, flexWidth: 1);
            }
            else if (_viewSourceMode || page.InteractiveRevealed)
            {
                var hintTxt = UITheme.CreateText(
                    _interactiveArea,
                    "ClueDetected",
                    $"Обнаружен фрагмент ключа #{frag.SlotIndex}: <color=#FFB84D><b>{frag.RawValue}</b></color> ({KeyDecoderUtil.GetEncodingLabel(frag.Encoding)})",
                    18,
                    UITheme.TextPrimary);
                UITheme.AddLayout(hintTxt, flexWidth: 1);

                var grabBtn = UITheme.CreateButton(
                    _interactiveArea,
                    "GrabKeyBtn",
                    "Забрать фрагмент в Декодер",
                    UITheme.AccentGreen,
                    UITheme.TextDark,
                    18,
                    () =>
                    {
                        gm.DiscoverFragment(frag.Id);
                        Refresh();
                    });
                UITheme.AddLayout(grabBtn, prefWidth: 330);
            }
            else
            {
                var tipTxt = UITheme.CreateText(
                    _interactiveArea,
                    "TipSource",
                    "На видимой части страницы ключа нет. Переключите «</> Исходный код», чтобы проверить скрытые теги и комментарии.",
                    17,
                    UITheme.TextMuted);
                UITheme.AddLayout(tipTxt, flexWidth: 1);

                var openSrcBtn = UITheme.CreateButton(
                    _interactiveArea,
                    "OpenSrcQuick",
                    "</> Инспектировать код",
                    UITheme.AccentViolet,
                    UITheme.TextPrimary,
                    17,
                    () =>
                    {
                        _viewSourceMode = true;
                        Refresh();
                    });
                UITheme.AddLayout(openSrcBtn, prefWidth: 260);
            }
        }

        private static string EscapeHtmlForRichText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            // Заменяем угловые скобки, чтобы TMP RichText не съел HTML-теги, и подсвечиваем комментарии
            string safe = html.Replace("<", "&lt;").Replace(">", "&gt;");
            safe = Regex.Replace(safe, @"&lt;!--(.*?)--&gt;", "<color=#2EE676>&lt;!--$1--&gt;</color>");
            return safe;
        }
    }
}
