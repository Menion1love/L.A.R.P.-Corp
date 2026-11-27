using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Trash» (Корзина):
    /// просмотр удалённых файлов, восстановление в исходную папку, удаление навсегда и очистка корзины.
    /// </summary>
    public class TrashWindowApp : MonoBehaviour
    {
        private VirtualFileSystem _vfs;
        private LarpDesktopOS _desktop;
        private RectTransform _listContent;
        private TextMeshProUGUI _statusText;
        private Button _restoreBtn;
        private Button _deleteBtn;
        private Button _emptyBtn;

        private string _selectedId;
        private float _lastClickTime;
        private string _lastClickedId;

        public void Initialize(LarpDesktopOS desktop, VirtualFileSystem vfs, RectTransform root)
        {
            _desktop = desktop;
            _vfs = vfs;
            _vfs.OnFileSystemChanged += Refresh;

            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 0;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // Панель действий корзины
            var toolbar = UITheme.CreatePanel(root, "TrashToolbar", UITheme.LightToolbarBg);
            UITheme.AddLayout(toolbar, prefHeight: 42);
            var h = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.padding = new RectOffset(10, 10, 5, 5);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;

            _restoreBtn = UITheme.CreateButton(toolbar, "RestoreBtn", "Восстановить", UITheme.AccentGreen, UITheme.TextPrimary, 14f, () =>
            {
                if (!string.IsNullOrEmpty(_selectedId) && _vfs.RestoreFromTrash(_selectedId))
                {
                    _selectedId = null;
                    Refresh();
                }
            });
            UITheme.AddLayout(_restoreBtn, prefWidth: 132);

            _deleteBtn = UITheme.CreateButton(toolbar, "DelPermBtn", "Удалить навсегда", UITheme.LightWindowBg, UITheme.TextDark, 14f, () =>
            {
                if (!string.IsNullOrEmpty(_selectedId) && _vfs.DeletePermanently(_selectedId))
                {
                    _selectedId = null;
                    Refresh();
                }
            });
            UITheme.AddLayout(_deleteBtn, prefWidth: 152);

            var spacer = UITheme.CreatePanel(toolbar, "Spacer", new Color(0, 0, 0, 0));
            UITheme.AddLayout(spacer, flexWidth: 1);

            _emptyBtn = UITheme.CreateButton(toolbar, "EmptyBtn", "Очистить корзину", new Color(0.82f, 0.28f, 0.24f, 1f), UITheme.TextPrimary, 14f, () =>
            {
                _selectedId = null;
                _vfs.EmptyTrash();
                Refresh();
            });
            UITheme.AddLayout(_emptyBtn, prefWidth: 156);

            var sep1 = UITheme.CreatePanel(root, "Sep1", UITheme.LightDivider);
            UITheme.AddLayout(sep1, prefHeight: 1);

            var (scroll, content) = UITheme.CreateVerticalScrollView(root, "TrashScroll", UITheme.LightPaperBg, 3f, 8);
            UITheme.AddLayout(scroll, flexHeight: 1);
            _listContent = content;

            var sep2 = UITheme.CreatePanel(root, "Sep2", UITheme.LightDivider);
            UITheme.AddLayout(sep2, prefHeight: 1);

            var footer = UITheme.CreatePanel(root, "Footer", UITheme.LightToolbarBg);
            UITheme.AddLayout(footer, prefHeight: 26);
            _statusText = UITheme.CreateText(footer, "Status", "", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
            UITheme.StretchFill(_statusText.rectTransform, 10, 0, 10, 0);

            Refresh();
        }

        private void OnDestroy()
        {
            if (_vfs != null)
                _vfs.OnFileSystemChanged -= Refresh;
        }

        public void Refresh()
        {
            if (_listContent == null || _vfs == null) return;

            var items = _vfs.GetTrashEntries();
            UITheme.ClearChildren(_listContent);

            if (items.Count == 0)
            {
                var empty = UITheme.CreateText(_listContent, "Empty", "Корзина пуста.", 16f, UITheme.TextDarkMuted, TextAlignmentOptions.Center);
                UITheme.AddLayout(empty, prefHeight: 120);
            }
            else
            {
                foreach (var entry in items)
                {
                    bool selected = entry.Id == _selectedId;
                    var row = UITheme.CreatePanel(_listContent, "TRow_" + entry.Name, selected ? UITheme.LightItemSelected : new Color(0, 0, 0, 0));
                    UITheme.AddLayout(row, prefHeight: 38);

                    var btn = row.gameObject.AddComponent<Button>();
                    btn.targetGraphic = row.GetComponent<Image>();
                    var colors = btn.colors;
                    colors.highlightedColor = UITheme.LightItemHover;
                    btn.colors = colors;

                    VfsEntry captured = entry;
                    btn.onClick.AddListener(() =>
                    {
                        float now = Time.unscaledTime;
                        if (_lastClickedId == captured.Id && (now - _lastClickTime) < 0.38f)
                        {
                            _lastClickedId = null;
                            AudioFeedbackManager.Instance?.PlayClick();
                            _desktop?.OpenTextViewer(captured.Name + " [Trash]", captured.FullPath, captured.Content);
                            return;
                        }

                        _lastClickedId = captured.Id;
                        _lastClickTime = now;
                        _selectedId = captured.Id;
                        AudioFeedbackManager.Instance?.PlayClick();
                        Refresh();
                    });

                    var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    h.spacing = 10;
                    h.padding = new RectOffset(8, 12, 4, 4);
                    h.childControlWidth = true;
                    h.childControlHeight = true;
                    h.childForceExpandHeight = true;
                    h.childForceExpandWidth = false;

                    var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                    iconGo.transform.SetParent(row, false);
                    var iconImg = iconGo.GetComponent<Image>();
                    iconImg.sprite = entry.IsFolder ? OSIconFactory.GetFolderIcon() : OSIconFactory.GetFileIcon(entry.EntryType == VfsEntryType.LogFile);
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                    UITheme.AddLayout(iconImg, prefWidth: 26);

                    var nameTxt = UITheme.CreateText(row, "Name", entry.Name, 15f, UITheme.TextDark, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    UITheme.AddLayout(nameTxt, prefWidth: 230);

                    var origTxt = UITheme.CreateText(row, "Orig", "из: " + (entry.OriginalFolderPath ?? "/home/nullptr"), 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
                    UITheme.AddLayout(origTxt, flexWidth: 1);

                    var sizeTxt = UITheme.CreateText(row, "Size", VirtualFileSystem.FormatSize(entry.SizeBytes), 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineRight, FontStyles.Normal, monospace: true);
                    UITheme.AddLayout(sizeTxt, prefWidth: 74);
                }
            }

            bool hasSel = items.Any(e => e.Id == _selectedId);
            _restoreBtn.interactable = hasSel;
            _deleteBtn.interactable = hasSel;
            _emptyBtn.interactable = items.Count > 0;
            _statusText.text = $"В корзине: {items.Count} объектов  |  Двойной клик — просмотр";
        }
    }
}
