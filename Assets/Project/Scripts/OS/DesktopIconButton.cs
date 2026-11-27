using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Ярлык приложения на рабочем столе Linux:
    /// одиночный клик — выделение, двойной клик — открытие окна приложения.
    /// </summary>
    public class DesktopIconButton : MonoBehaviour, IPointerClickHandler
    {
        private Image _highlightBg;
        private Image _iconImage;
        private TextMeshProUGUI _labelText;
        private Action _onOpen;
        private Action<DesktopIconButton> _onSelected;
        private float _lastClickTime;

        public void Initialize(
            RectTransform parent,
            string title,
            Sprite icon,
            Vector2 anchoredPos,
            Action onOpen,
            Action<DesktopIconButton> onSelected)
        {
            _onOpen = onOpen;
            _onSelected = onSelected;

            var rt = GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(104f, 102f);
            rt.anchoredPosition = anchoredPos;

            _highlightBg = gameObject.AddComponent<Image>();
            _highlightBg.sprite = UITheme.WhiteSprite;
            _highlightBg.color = new Color(1, 1, 1, 0f);

            var v = gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4;
            v.padding = new RectOffset(6, 6, 6, 4);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rt, false);
            _iconImage = iconGo.GetComponent<Image>();
            _iconImage.sprite = icon;
            _iconImage.preserveAspect = true;
            _iconImage.raycastTarget = false;
            UITheme.AddLayout(_iconImage, prefHeight: 58f);

            _labelText = UITheme.CreateText(rt, "Label", title, 14f, UITheme.TextPrimary, TextAlignmentOptions.Top, FontStyles.Normal);
            _labelText.textWrappingMode = TextWrappingModes.Normal;
            UITheme.AddLayout(_labelText, prefHeight: 30f);

            var shadow = _labelText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        public void SetIcon(Sprite sprite)
        {
            if (_iconImage != null)
                _iconImage.sprite = sprite;
        }

        public void SetLabel(string title)
        {
            if (_labelText != null)
                _labelText.text = title;
        }

        public void SetSelected(bool selected)
        {
            if (_highlightBg != null)
                _highlightBg.color = selected ? new Color(0.85f, 0.41f, 0.16f, 0.30f) : new Color(1f, 1f, 1f, 0f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            float now = Time.unscaledTime;
            _onSelected?.Invoke(this);

            if (eventData.clickCount >= 2 || (now - _lastClickTime) < 0.38f)
            {
                _lastClickTime = -10f;
                AudioFeedbackManager.Instance?.PlayClick();
                _onOpen?.Invoke();
            }
            else
            {
                _lastClickTime = now;
                AudioFeedbackManager.Instance?.PlayClick();
            }
        }
    }
}
