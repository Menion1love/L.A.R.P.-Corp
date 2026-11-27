using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Полноценное окно Linux-подобной ОС:
    /// перетаскивание за шапку (1:1 в World Space Canvas), фокус по клику,
    /// сворачивание (-), разворачивание (□), закрытие (×) и изменение размера за правый нижний угол.
    /// </summary>
    public class OSWindow : MonoBehaviour, IPointerDownHandler
    {
        public string WindowId { get; private set; }
        public string Title { get; private set; }
        public RectTransform RootRect { get; private set; }
        public RectTransform ContentRoot { get; private set; }
        public bool IsMinimized { get; private set; }
        public bool IsMaximized { get; private set; }
        public bool IsOpen => gameObject.activeSelf || IsMinimized;

        private RectTransform _desktopBounds;
        private Image _titleBarBg;
        private TextMeshProUGUI _titleText;
        private OSWindowManager _manager;

        private Vector2 _minSize = new Vector2(360f, 260f);
        private Vector2 _savedAnchoredPos;
        private Vector2 _savedSize;
        private bool _isFocused;

        public event Action<OSWindow> OnClosed;

        public void Initialize(
            OSWindowManager manager,
            RectTransform desktopBounds,
            string windowId,
            string title,
            Vector2 initialSize,
            Vector2 initialPos,
            Color contentBg,
            Vector2? minSize = null,
            bool allowMaximize = true,
            bool allowResize = true)
        {
            _manager = manager;
            _desktopBounds = desktopBounds;
            WindowId = windowId;
            Title = title;
            if (minSize.HasValue) _minSize = minSize.Value;

            RootRect = GetComponent<RectTransform>();
            RootRect.anchorMin = new Vector2(0.5f, 0.5f);
            RootRect.anchorMax = new Vector2(0.5f, 0.5f);
            RootRect.pivot = new Vector2(0.5f, 0.5f);
            RootRect.sizeDelta = initialSize;
            RootRect.anchoredPosition = initialPos;

            // Внешняя тонкая рамка окна
            var frameImg = gameObject.AddComponent<Image>();
            frameImg.sprite = UITheme.WhiteSprite;
            frameImg.color = UITheme.WindowBorder;

            var inner = UITheme.CreatePanel(RootRect, "WindowInner", contentBg);
            UITheme.StretchFill(inner, 2, 2, 2, 2);

            // Шапка окна (32px) — как на референсе
            var titleBar = UITheme.CreatePanel(inner, "TitleBar", UITheme.WindowTitleActive);
            titleBar.anchorMin = new Vector2(0f, 1f);
            titleBar.anchorMax = new Vector2(1f, 1f);
            titleBar.pivot = new Vector2(0.5f, 1f);
            titleBar.anchoredPosition = Vector2.zero;
            titleBar.sizeDelta = new Vector2(0f, 34f);
            _titleBarBg = titleBar.GetComponent<Image>();

            // Тонкая оранжевая полоса под заголовком (стиль WTTG2 / Ubuntu)
            var orangeLine = UITheme.CreatePanel(titleBar, "AccentLine", UITheme.WindowOrange);
            orangeLine.anchorMin = new Vector2(0f, 0f);
            orangeLine.anchorMax = new Vector2(1f, 0f);
            orangeLine.pivot = new Vector2(0.5f, 0f);
            orangeLine.sizeDelta = new Vector2(0f, 2f);

            // Перетаскивание за заголовок
            var dragger = titleBar.gameObject.AddComponent<WindowDragHandler>();
            dragger.Setup(this, _desktopBounds);

            _titleText = UITheme.CreateText(titleBar, "TitleText", title, 17f, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            _titleText.rectTransform.anchorMin = Vector2.zero;
            _titleText.rectTransform.anchorMax = Vector2.one;
            _titleText.rectTransform.offsetMin = new Vector2(12f, 2f);
            _titleText.rectTransform.offsetMax = new Vector2(-104f, 0f);

            // Кнопки управления окном (- □ ×) в фирменном оранжевом стиле
            var btnRow = UITheme.CreatePanel(titleBar, "Controls", new Color(0, 0, 0, 0));
            btnRow.anchorMin = new Vector2(1f, 0f);
            btnRow.anchorMax = new Vector2(1f, 1f);
            btnRow.pivot = new Vector2(1f, 0.5f);
            btnRow.sizeDelta = new Vector2(96f, 0f);
            btnRow.anchoredPosition = new Vector2(-4f, 1f);

            var h = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4;
            h.padding = new RectOffset(2, 2, 5, 6);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleRight;

            CreateChromeButton(btnRow, "MinBtn", "-", Minimize);
            if (allowMaximize)
                CreateChromeButton(btnRow, "MaxBtn", "[]", ToggleMaximize);
            CreateChromeButton(btnRow, "CloseBtn", "X", Close);

            // Контент окна ниже шапки
            ContentRoot = UITheme.CreatePanel(inner, "Content", contentBg);
            UITheme.StretchFill(ContentRoot, 0, 0, 0, 34);

            // Уголок ресайза (правый нижний) — спрайт вместо Unicode-символа, чтобы не было предупреждений TMP
            if (allowResize)
            {
                var resizeGrip = UITheme.CreatePanel(inner, "ResizeGrip", new Color(0, 0, 0, 0));
                resizeGrip.anchorMin = new Vector2(1f, 0f);
                resizeGrip.anchorMax = new Vector2(1f, 0f);
                resizeGrip.pivot = new Vector2(1f, 0f);
                resizeGrip.sizeDelta = new Vector2(16f, 16f);
                resizeGrip.anchoredPosition = new Vector2(-1f, 1f);

                var gripImg = resizeGrip.GetComponent<Image>();
                gripImg.sprite = OSIconFactory.GetResizeGripIcon(new Color(0.55f, 0.58f, 0.62f, 0.85f));
                gripImg.color = Color.white;

                var resizer = resizeGrip.gameObject.AddComponent<WindowResizeHandler>();
                resizer.Setup(this, _desktopBounds, _minSize);
            }

            ClampInsideBounds();
        }

        private void CreateChromeButton(Transform parent, string name, string symbol, Action onClick)
        {
            var btn = UITheme.CreateButton(parent, name, symbol, UITheme.WindowOrange, UITheme.TextPrimary, 15f, onClick);
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
            {
                lbl.rectTransform.offsetMin = Vector2.zero;
                lbl.rectTransform.offsetMax = Vector2.zero;
            }
        }

        public void SetTitle(string newTitle)
        {
            Title = newTitle;
            if (_titleText != null) _titleText.text = newTitle;
            _manager?.NotifyWindowsChanged();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _manager?.FocusWindow(this);
        }

        public void SetFocusedVisual(bool focused)
        {
            _isFocused = focused;
            if (_titleBarBg != null)
                _titleBarBg.color = focused ? UITheme.WindowTitleActive : UITheme.WindowTitleIdle;
            if (_titleText != null)
                _titleText.color = focused ? UITheme.TextPrimary : UITheme.TextMuted;
        }

        public bool IsFocused => _isFocused;

        public void Minimize()
        {
            IsMinimized = true;
            gameObject.SetActive(false);
            _manager?.OnWindowMinimized(this);
        }

        public void RestoreFromMinimize()
        {
            IsMinimized = false;
            gameObject.SetActive(true);
            _manager?.FocusWindow(this);
        }

        public void ToggleMaximize()
        {
            if (_desktopBounds == null) return;

            if (!IsMaximized)
            {
                _savedAnchoredPos = RootRect.anchoredPosition;
                _savedSize = RootRect.sizeDelta;
                IsMaximized = true;

                Rect r = _desktopBounds.rect;
                RootRect.sizeDelta = new Vector2(r.width - 8f, r.height - 8f);
                RootRect.anchoredPosition = Vector2.zero;
            }
            else
            {
                IsMaximized = false;
                RootRect.sizeDelta = _savedSize;
                RootRect.anchoredPosition = _savedAnchoredPos;
                ClampInsideBounds();
            }

            _manager?.FocusWindow(this);
        }

        public void Close()
        {
            IsMinimized = false;
            IsMaximized = false;
            gameObject.SetActive(false);
            OnClosed?.Invoke(this);
            _manager?.OnWindowClosed(this);
        }

        public void ClampInsideBounds()
        {
            if (_desktopBounds == null || RootRect == null) return;

            Rect b = _desktopBounds.rect;
            Vector2 halfSize = RootRect.sizeDelta * 0.5f;

            // Оставляем как минимум шапку окна видимой внутри рабочего стола
            float minX = b.xMin - halfSize.x + 80f;
            float maxX = b.xMax + halfSize.x - 80f;
            float minY = b.yMin - halfSize.y + 42f;
            float maxY = b.yMax - halfSize.y;

            Vector2 pos = RootRect.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            RootRect.anchoredPosition = pos;
        }

        private class WindowDragHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
        {
            private OSWindow _window;
            private RectTransform _bounds;
            private Vector2 _pointerOffset;

            public void Setup(OSWindow window, RectTransform bounds)
            {
                _window = window;
                _bounds = bounds;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                _window._manager?.FocusWindow(_window);
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_window.IsMaximized)
                    _window.ToggleMaximize();

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds, eventData.position, eventData.pressEventCamera, out Vector2 localCursor))
                {
                    _pointerOffset = _window.RootRect.anchoredPosition - localCursor;
                }
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds, eventData.position, eventData.pressEventCamera, out Vector2 localCursor))
                {
                    _window.RootRect.anchoredPosition = localCursor + _pointerOffset;
                    _window.ClampInsideBounds();
                }
            }
        }

        private class WindowResizeHandler : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
        {
            private OSWindow _window;
            private RectTransform _bounds;
            private Vector2 _minSize;
            private Vector2 _startCursorLocal;
            private Vector2 _startSize;
            private Vector2 _startPos;

            public void Setup(OSWindow window, RectTransform bounds, Vector2 minSize)
            {
                _window = window;
                _bounds = bounds;
                _minSize = minSize;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                _window._manager?.FocusWindow(_window);
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_window.IsMaximized) return;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds, eventData.position, eventData.pressEventCamera, out _startCursorLocal);
                _startSize = _window.RootRect.sizeDelta;
                _startPos = _window.RootRect.anchoredPosition;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_window.IsMaximized) return;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds, eventData.position, eventData.pressEventCamera, out Vector2 cur))
                {
                    Vector2 delta = cur - _startCursorLocal;
                    Rect b = _bounds.rect;

                    float newW = Mathf.Clamp(_startSize.x + delta.x, _minSize.x, b.width - 20f);
                    float newH = Mathf.Clamp(_startSize.y - delta.y, _minSize.y, b.height - 20f);

                    float dw = newW - _startSize.x;
                    float dh = newH - _startSize.y;

                    // Оставляем верхний левый угол окна на месте при изменении размера
                    _window.RootRect.sizeDelta = new Vector2(newW, newH);
                    _window.RootRect.anchoredPosition = new Vector2(_startPos.x + dw * 0.5f, _startPos.y - dh * 0.5f);
                    _window.ClampInsideBounds();
                }
            }
        }
    }

    /// <summary>
    /// Менеджер окон рабочего стола: хранит список открытых/свёрнутых окон, управляет Z-порядком и синхронизацией с таскбаром.
    /// </summary>
    public class OSWindowManager
    {
        private readonly RectTransform _windowsLayer;
        private readonly List<OSWindow> _windows = new List<OSWindow>();
        private OSWindow _focusedWindow;
        private int _cascadeIndex;

        public IReadOnlyList<OSWindow> Windows => _windows;
        public OSWindow FocusedWindow => _focusedWindow;

        public event Action OnWindowsChanged;

        public OSWindowManager(RectTransform windowsLayer)
        {
            _windowsLayer = windowsLayer;
        }

        public OSWindow FindWindow(string windowId)
        {
            return _windows.FirstOrDefault(w => w != null && w.WindowId == windowId);
        }

        public OSWindow CreateOrFocusWindow(
            string windowId,
            string title,
            Vector2 size,
            Color contentBg,
            Action<RectTransform, OSWindow> buildContent,
            Vector2? explicitPos = null,
            Vector2? minSize = null,
            bool allowMaximize = true,
            bool allowResize = true)
        {
            var existing = FindWindow(windowId);
            if (existing != null)
            {
                existing.RestoreFromMinimize();
                return existing;
            }

            var go = new GameObject("Win_" + windowId, typeof(RectTransform));
            go.transform.SetParent(_windowsLayer, false);
            var win = go.AddComponent<OSWindow>();

            Vector2 pos = explicitPos ?? new Vector2(-120f + (_cascadeIndex % 6) * 42f, 60f - (_cascadeIndex % 6) * 34f);
            _cascadeIndex++;

            win.Initialize(this, _windowsLayer, windowId, title, size, pos, contentBg, minSize, allowMaximize, allowResize);
            _windows.Add(win);
            buildContent?.Invoke(win.ContentRoot, win);
            FocusWindow(win);
            return win;
        }

        public void FocusWindow(OSWindow win)
        {
            if (win == null) return;
            win.transform.SetAsLastSibling();
            _focusedWindow = win;

            foreach (var w in _windows)
            {
                if (w != null) w.SetFocusedVisual(w == win && !w.IsMinimized && w.gameObject.activeSelf);
            }

            OnWindowsChanged?.Invoke();
        }

        public void ToggleFromTaskbar(OSWindow win)
        {
            if (win == null) return;

            if (win.IsMinimized || !win.gameObject.activeSelf)
            {
                win.RestoreFromMinimize();
            }
            else if (_focusedWindow == win)
            {
                win.Minimize();
            }
            else
            {
                FocusWindow(win);
            }
        }

        public void OnWindowMinimized(OSWindow win)
        {
            if (_focusedWindow == win)
            {
                _focusedWindow = _windows.LastOrDefault(w => w != null && w != win && !w.IsMinimized && w.gameObject.activeSelf);
                if (_focusedWindow != null) _focusedWindow.SetFocusedVisual(true);
            }
            OnWindowsChanged?.Invoke();
        }

        public void OnWindowClosed(OSWindow win)
        {
            if (_focusedWindow == win)
            {
                _focusedWindow = _windows.LastOrDefault(w => w != null && w != win && !w.IsMinimized && w.gameObject.activeSelf);
                if (_focusedWindow != null) _focusedWindow.SetFocusedVisual(true);
            }
            OnWindowsChanged?.Invoke();
        }

        public void NotifyWindowsChanged()
        {
            OnWindowsChanged?.Invoke();
        }
    }
}
