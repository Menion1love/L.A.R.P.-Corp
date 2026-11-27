using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.LinuxOS
{
    /// <summary>
    /// Чистое окно просмотра текста / исходного кода (как Source Viewer на втором референсе).
    /// </summary>
    public class TextViewerWindowApp : MonoBehaviour
    {
        private TextMeshProUGUI _contentText;
        private TextMeshProUGUI _footerText;

        public void Initialize(RectTransform root, string pathLabel, string content)
        {
            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 0;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var (scroll, contentRt) = UITheme.CreateVerticalScrollView(root, "TextScroll", UITheme.LightPaperBg, 4f, 14);
            UITheme.AddLayout(scroll, flexHeight: 1);

            _contentText = UITheme.CreateText(contentRt, "DocText", content ?? string.Empty, 15f, UITheme.TextDark, TextAlignmentOptions.TopLeft, FontStyles.Normal, monospace: true);
            var csf = _contentText.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sep = UITheme.CreatePanel(root, "Sep", UITheme.LightDivider);
            UITheme.AddLayout(sep, prefHeight: 1);

            var footer = UITheme.CreatePanel(root, "Footer", UITheme.LightToolbarBg);
            UITheme.AddLayout(footer, prefHeight: 28);
            _footerText = UITheme.CreateText(footer, "Path", pathLabel ?? "", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
            UITheme.StretchFill(_footerText.rectTransform, 10, 0, 10, 0);
        }

        public void SetContent(string pathLabel, string content)
        {
            if (_contentText != null) _contentText.text = content ?? string.Empty;
            if (_footerText != null) _footerText.text = pathLabel ?? string.Empty;
        }
    }
}
