using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Палитра и фабрика UI-элементов в стиле реального Linux-десктопа (Xfce / Ubuntu / WTTG2):
    /// матовый сланцевый фон рабочего стола, тёмные заголовки окон, тёплый оранжевый акцент (#D9682A),
    /// светлые читаемые рабочие панели и меню.
    /// Шрифты: Bahnschrift (узкий игровой/UI-гротеск) + JetBrains Mono (терминал/код/логи).
    /// </summary>
    public static class UITheme
    {
        // --- Основные поверхности Linux OS ---
        public static readonly Color WallpaperSlate    = new Color(0.23f, 0.27f, 0.31f, 1.00f); // #3B454F
        public static readonly Color TaskbarBg         = new Color(0.08f, 0.08f, 0.09f, 1.00f); // #141417
        public static readonly Color TaskbarTabActive  = new Color(0.18f, 0.19f, 0.21f, 1.00f);
        public static readonly Color TaskbarTabIdle    = new Color(0.12f, 0.12f, 0.14f, 1.00f);

        public static readonly Color WindowTitleActive = new Color(0.11f, 0.11f, 0.12f, 1.00f); // #1C1C1F
        public static readonly Color WindowTitleIdle   = new Color(0.17f, 0.18f, 0.20f, 1.00f);
        public static readonly Color WindowBorder      = new Color(0.05f, 0.05f, 0.06f, 1.00f);
        public static readonly Color WindowOrange      = new Color(0.85f, 0.41f, 0.16f, 1.00f); // #D9682A
        public static readonly Color WindowOrangeHover = new Color(0.95f, 0.50f, 0.23f, 1.00f);

        // Светлые поверхности (Notes, My Computer, Wi-Fi Menu, Source Viewer)
        public static readonly Color LightWindowBg     = new Color(0.93f, 0.94f, 0.95f, 1.00f); // #EDEFF2
        public static readonly Color LightPaperBg      = new Color(0.97f, 0.98f, 0.98f, 1.00f); // #F8FAFA
        public static readonly Color LightSidebarBg    = new Color(0.86f, 0.87f, 0.89f, 1.00f); // #DBDDE3
        public static readonly Color LightToolbarBg    = new Color(0.82f, 0.84f, 0.86f, 1.00f); // #D1D6DB
        public static readonly Color LightItemHover    = new Color(0.76f, 0.82f, 0.88f, 1.00f);
        public static readonly Color LightItemSelected = new Color(0.85f, 0.41f, 0.16f, 0.24f);
        public static readonly Color LightDivider      = new Color(0.72f, 0.74f, 0.77f, 1.00f);

        // Тёмные поверхности (Terminal / Bruteforce / DarkNet / HUD)
        public static readonly Color TerminalBg        = new Color(0.09f, 0.09f, 0.10f, 1.00f); // #17171A
        public static readonly Color TerminalGreen     = new Color(0.24f, 0.78f, 0.34f, 1.00f);

        // Совместимые алиасы для существующих экранов (HUD, игровые окна)
        public static readonly Color BgDesktop         = new Color(0.11f, 0.12f, 0.14f, 1.00f);
        public static readonly Color BgTopBar          = TaskbarBg;
        public static readonly Color BgSidebar         = new Color(0.14f, 0.15f, 0.17f, 1.00f);
        public static readonly Color BgPanel           = new Color(0.16f, 0.17f, 0.19f, 0.98f);
        public static readonly Color BgCard            = new Color(0.20f, 0.21f, 0.24f, 1.00f);
        public static readonly Color BgCardHover       = new Color(0.25f, 0.27f, 0.30f, 1.00f);
        public static readonly Color BgInput           = new Color(0.09f, 0.10f, 0.11f, 1.00f);
        public static readonly Color BgModalDim        = new Color(0.04f, 0.04f, 0.05f, 0.85f);

        public static readonly Color AccentCyan        = WindowOrange;                          // Единый строгий акцент
        public static readonly Color AccentGreen       = new Color(0.24f, 0.72f, 0.38f, 1.00f);
        public static readonly Color AccentAmber       = new Color(0.88f, 0.62f, 0.20f, 1.00f);
        public static readonly Color AccentRed         = new Color(0.82f, 0.26f, 0.26f, 1.00f);
        public static readonly Color AccentViolet      = new Color(0.45f, 0.48f, 0.56f, 1.00f);

        public static readonly Color TextPrimary       = new Color(0.92f, 0.93f, 0.95f, 1.00f);
        public static readonly Color TextMuted         = new Color(0.62f, 0.65f, 0.70f, 1.00f);
        public static readonly Color TextDark          = new Color(0.11f, 0.12f, 0.14f, 1.00f);
        public static readonly Color TextDarkMuted     = new Color(0.38f, 0.41f, 0.46f, 1.00f);

        private static TMP_FontAsset _uiFont;
        private static TMP_FontAsset _monoFont;
        private static Sprite _whiteSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _uiFont = null;
            _monoFont = null;
            _whiteSprite = null;
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                    tex.Apply();
                    _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
                }
                return _whiteSprite;
            }
        }

        /// <summary>
        /// Основной игровой UI-шрифт (узкий индустриальный Bahnschrift из TTF-файла Windows с полной кириллицей, без предупреждений TMP).
        /// </summary>
        public static TMP_FontAsset GetUiFont()
        {
            if (_uiFont != null)
                return _uiFont;

            string[] candidatePaths =
            {
                @"C:\Windows\Fonts\bahnschrift.ttf",
                @"C:\Windows\Fonts\segoeui.ttf",
                @"C:\Windows\Fonts\arial.ttf"
            };

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string fontPath = candidatePaths[i];
                if (File.Exists(fontPath))
                {
                    var created = TMP_FontAsset.CreateFontAsset(
                        fontPath, 0, 48, 6, GlyphRenderMode.SDFAA, 1024, 1024);
                    if (created != null)
                    {
                        _uiFont = created;
                        return _uiFont;
                    }
                }
            }

            _uiFont = GetMonoFont();
            return _uiFont;
        }

        /// <summary>
        /// Моноширинный шрифт для терминала, кода, логов и путей файлов (JetBrains Mono / Consolas).
        /// </summary>
        public static TMP_FontAsset GetMonoFont()
        {
            if (_monoFont != null)
                return _monoFont;

#if UNITY_EDITOR
            _monoFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/Project/UI/JetBrainsMono-Regular SDF.asset");
#endif

            if (_monoFont == null)
            {
                string[] monoPaths =
                {
                    @"C:\Windows\Fonts\consola.ttf",
                    @"C:\Windows\Fonts\cour.ttf"
                };

                for (int i = 0; i < monoPaths.Length; i++)
                {
                    if (File.Exists(monoPaths[i]))
                    {
                        var created = TMP_FontAsset.CreateFontAsset(
                            monoPaths[i], 0, 44, 6, GlyphRenderMode.SDFAA, 1024, 1024);
                        if (created != null)
                        {
                            _monoFont = created;
                            return _monoFont;
                        }
                    }
                }
            }

            if (_monoFont == null)
                _monoFont = TMP_Settings.defaultFontAsset;

            return _monoFont;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite;
            img.color = color;
            return go.GetComponent<RectTransform>();
        }

        public static void StretchFill(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string content,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            FontStyles style = FontStyles.Normal,
            bool monospace = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = monospace ? GetMonoFont() : GetUiFont();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Color bgColor,
            Color textColor,
            float fontSize = 16f,
            System.Action onClick = null,
            bool monospace = false)
        {
            var rt = CreatePanel(parent, name, bgColor);
            var img = rt.GetComponent<Image>();
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.6f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;

            var tmp = CreateText(rt, "Label", label, fontSize, textColor, TextAlignmentOptions.Center, FontStyles.Bold, monospace);
            StretchFill(tmp.rectTransform, 8, 2, 8, 2);

            if (onClick != null)
            {
                btn.onClick.AddListener(() =>
                {
                    AudioFeedbackManager.Instance?.PlayClick();
                    onClick.Invoke();
                });
            }

            return btn;
        }

        public static TMP_InputField CreateInputField(
            Transform parent,
            string name,
            string placeholderText,
            float fontSize = 16f,
            bool lightStyle = false,
            bool monospace = false)
        {
            var root = CreatePanel(parent, name, lightStyle ? LightPaperBg : BgInput);
            var input = root.gameObject.AddComponent<TMP_InputField>();

            if (lightStyle)
            {
                var outline = root.gameObject.AddComponent<Outline>();
                outline.effectColor = LightDivider;
                outline.effectDistance = new Vector2(1, -1);
            }

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root, false);
            var vpRt = viewport.GetComponent<RectTransform>();
            StretchFill(vpRt, 10, 4, 10, 4);

            Color textCol = lightStyle ? TextDark : TextPrimary;
            Color phCol = lightStyle ? TextDarkMuted : TextMuted;

            var placeholder = CreateText(vpRt, "Placeholder", placeholderText, fontSize, phCol, TextAlignmentOptions.MidlineLeft, FontStyles.Italic, monospace);
            StretchFill(placeholder.rectTransform);

            var text = CreateText(vpRt, "Text", string.Empty, fontSize, textCol, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace);
            StretchFill(text.rectTransform);

            input.textViewport = vpRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = monospace ? GetMonoFont() : GetUiFont();
            input.pointSize = fontSize;
            input.caretColor = WindowOrange;
            input.selectionColor = new Color(0.85f, 0.41f, 0.16f, 0.30f);

            return input;
        }

        public static (ScrollRect scroll, RectTransform content) CreateVerticalScrollView(
            Transform parent,
            string name,
            Color bgColor,
            float spacing = 6f,
            int pad = 8)
        {
            var root = CreatePanel(parent, name, bgColor);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            vpGo.transform.SetParent(root, false);
            var vpRt = vpGo.GetComponent<RectTransform>();
            StretchFill(vpRt);

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(vpRt, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(pad, pad, pad, pad);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = vpRt;
            scroll.content = contentRt;

            return (scroll, contentRt);
        }

        public static (RectTransform root, Image fill, TextMeshProUGUI label) CreateProgressBar(
            Transform parent,
            string name,
            Color bgColor,
            Color fillColor,
            string initialLabel = "",
            float fontSize = 14f)
        {
            var root = CreatePanel(parent, name, bgColor);
            var fillRt = CreatePanel(root, "Fill", fillColor);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            var label = CreateText(root, "Label", initialLabel, fontSize, TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold, monospace: true);
            StretchFill(label.rectTransform, 6, 0, 6, 0);

            return (root, fillRt.GetComponent<Image>(), label);
        }

        public static void SetBarFill(Image fill, float normalized)
        {
            if (fill == null) return;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        }

        public static LayoutElement AddLayout(Component c, float prefHeight = -1, float prefWidth = -1, float flexWidth = -1, float flexHeight = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (prefHeight >= 0) le.preferredHeight = prefHeight;
            if (prefWidth >= 0) le.preferredWidth = prefWidth;
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return le;
        }

        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
