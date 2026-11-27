using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Проводник Linux — «My Computer»:
    /// боковая панель разделов (Home, Desktop, Documents, Downloads, Logs, Trash),
    /// адресная строка пути, создание новых файлов, удаление в корзину, открытие файлов двойным кликом.
    /// </summary>
    public class FileExplorerWindowApp : MonoBehaviour
    {
        private VirtualFileSystem _vfs;
        private LarpDesktopOS _desktop;

        private string _currentPath = VirtualFileSystem.HomePath;
        private string _selectedEntryId;
        private float _lastClickTime;
        private string _lastClickedId;
        private readonly Stack<string> _history = new Stack<string>();

        private TextMeshProUGUI _pathText;
        private TextMeshProUGUI _statusText;
        private RectTransform _listContent;
        private Button _backBtn;
        private Button _openBtn;
        private Button _deleteBtn;

        public void Initialize(LarpDesktopOS desktop, VirtualFileSystem vfs, RectTransform root)
        {
            _desktop = desktop;
            _vfs = vfs;
            _vfs.OnFileSystemChanged += Refresh;

            var outerV = root.gameObject.AddComponent<VerticalLayoutGroup>();
            outerV.spacing = 0;
            outerV.childControlWidth = true;
            outerV.childControlHeight = true;
            outerV.childForceExpandWidth = true;
            outerV.childForceExpandHeight = false;

            // --- Верхняя панель навигации ---
            var toolbar = UITheme.CreatePanel(root, "Toolbar", UITheme.LightToolbarBg);
            UITheme.AddLayout(toolbar, prefHeight: 42);
            var th = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 6;
            th.padding = new RectOffset(8, 8, 5, 5);
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandHeight = true;
            th.childForceExpandWidth = false;

            _backBtn = UITheme.CreateButton(toolbar, "BackBtn", "←", UITheme.LightWindowBg, UITheme.TextDark, 16f, GoBack);
            UITheme.AddLayout(_backBtn, prefWidth: 36);

            var upBtn = UITheme.CreateButton(toolbar, "UpBtn", "↑", UITheme.LightWindowBg, UITheme.TextDark, 16f, () =>
            {
                NavigateTo(VirtualFileSystem.GetParentPath(_currentPath), pushHistory: true);
            });
            UITheme.AddLayout(upBtn, prefWidth: 36);

            var pathBox = UITheme.CreatePanel(toolbar, "PathBox", UITheme.LightPaperBg);
            UITheme.AddLayout(pathBox, flexWidth: 1);
            _pathText = UITheme.CreateText(pathBox, "PathTxt", _currentPath, 15f, UITheme.TextDark, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
            UITheme.StretchFill(_pathText.rectTransform, 10, 0, 10, 0);

            _openBtn = UITheme.CreateButton(toolbar, "OpenBtn", "Открыть", UITheme.LightWindowBg, UITheme.TextDark, 14f, OpenSelected);
            UITheme.AddLayout(_openBtn, prefWidth: 92);

            var newFileBtn = UITheme.CreateButton(toolbar, "NewFileBtn", "+ Файл", UITheme.LightWindowBg, UITheme.TextDark, 14f, CreateNewTextFile);
            UITheme.AddLayout(newFileBtn, prefWidth: 82);

            _deleteBtn = UITheme.CreateButton(toolbar, "DeleteBtn", "В корзину", new Color(0.84f, 0.34f, 0.28f, 1f), UITheme.TextPrimary, 14f, DeleteSelected);
            UITheme.AddLayout(_deleteBtn, prefWidth: 102);

            var sep1 = UITheme.CreatePanel(root, "Sep1", UITheme.LightDivider);
            UITheme.AddLayout(sep1, prefHeight: 1);

            // --- Центр: Боковая панель + Список файлов ---
            var body = UITheme.CreatePanel(root, "Body", UITheme.LightWindowBg);
            UITheme.AddLayout(body, flexHeight: 1);
            var bh = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 0;
            bh.childControlWidth = true;
            bh.childControlHeight = true;
            bh.childForceExpandHeight = true;
            bh.childForceExpandWidth = false;

            // Боковая панель Places (как в Thunar / Nautilus)
            var sidebar = UITheme.CreatePanel(body, "PlacesSidebar", UITheme.LightSidebarBg);
            UITheme.AddLayout(sidebar, prefWidth: 168);
            var sv = sidebar.gameObject.AddComponent<VerticalLayoutGroup>();
            sv.spacing = 4;
            sv.padding = new RectOffset(8, 8, 10, 10);
            sv.childControlWidth = true;
            sv.childControlHeight = true;
            sv.childForceExpandWidth = true;
            sv.childForceExpandHeight = false;

            var placesTitle = UITheme.CreateText(sidebar, "PlacesHdr", "PLACES", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(placesTitle, prefHeight: 22);

            AddSidebarPlace(sidebar, "Home", VirtualFileSystem.HomePath);
            AddSidebarPlace(sidebar, "Desktop", VirtualFileSystem.DesktopPath);
            AddSidebarPlace(sidebar, "Documents", VirtualFileSystem.DocumentsPath);
            AddSidebarPlace(sidebar, "Downloads", VirtualFileSystem.DownloadsPath);
            AddSidebarPlace(sidebar, "Logs", VirtualFileSystem.LogsPath);

            var sbSep = UITheme.CreatePanel(sidebar, "Sep", UITheme.LightDivider);
            UITheme.AddLayout(sbSep, prefHeight: 1);

            var trashBtn = UITheme.CreateButton(sidebar, "Place_Trash", "Trash", new Color(0, 0, 0, 0), UITheme.TextDark, 15f, () =>
            {
                _desktop?.OpenTrashWindow();
            });
            UITheme.AddLayout(trashBtn, prefHeight: 32);
            var trashLbl = trashBtn.GetComponentInChildren<TextMeshProUGUI>();
            trashLbl.alignment = TextAlignmentOptions.MidlineLeft;
            trashLbl.fontStyle = FontStyles.Normal;

            var vSep = UITheme.CreatePanel(body, "VSep", UITheme.LightDivider);
            UITheme.AddLayout(vSep, prefWidth: 1);

            // Правая часть: заголовок таблицы + скролл файлов
            var rightCol = UITheme.CreatePanel(body, "FilesCol", UITheme.LightPaperBg);
            UITheme.AddLayout(rightCol, flexWidth: 1);
            var rv = rightCol.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 0;
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;

            var colHeader = UITheme.CreatePanel(rightCol, "ColHeader", UITheme.LightWindowBg);
            UITheme.AddLayout(colHeader, prefHeight: 28);
            var ch = colHeader.gameObject.AddComponent<HorizontalLayoutGroup>();
            ch.spacing = 8;
            ch.padding = new RectOffset(44, 14, 0, 0);
            ch.childControlWidth = true;
            ch.childControlHeight = true;
            ch.childForceExpandHeight = true;
            ch.childForceExpandWidth = false;

            var hName = UITheme.CreateText(colHeader, "HName", "Имя", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(hName, flexWidth: 1);
            var hSize = UITheme.CreateText(colHeader, "HSize", "Размер", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            UITheme.AddLayout(hSize, prefWidth: 90);
            var hMod = UITheme.CreateText(colHeader, "HMod", "Изменён", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            UITheme.AddLayout(hMod, prefWidth: 90);

            var (scroll, content) = UITheme.CreateVerticalScrollView(rightCol, "FilesScroll", UITheme.LightPaperBg, 2f, 4);
            UITheme.AddLayout(scroll, flexHeight: 1);
            _listContent = content;

            // --- Нижняя строка состояния ---
            var sep2 = UITheme.CreatePanel(root, "Sep2", UITheme.LightDivider);
            UITheme.AddLayout(sep2, prefHeight: 1);

            var statusBar = UITheme.CreatePanel(root, "StatusBar", UITheme.LightToolbarBg);
            UITheme.AddLayout(statusBar, prefHeight: 26);
            _statusText = UITheme.CreateText(statusBar, "StatusTxt", "", 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
            UITheme.StretchFill(_statusText.rectTransform, 10, 0, 10, 0);

            Refresh();
        }

        private void OnDestroy()
        {
            if (_vfs != null)
                _vfs.OnFileSystemChanged -= Refresh;
        }

        private void AddSidebarPlace(Transform parent, string label, string targetPath)
        {
            var btn = UITheme.CreateButton(parent, "Place_" + label, label, new Color(0, 0, 0, 0), UITheme.TextDark, 15f, () =>
            {
                NavigateTo(targetPath, pushHistory: true);
            });
            UITheme.AddLayout(btn, prefHeight: 32);
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.fontStyle = FontStyles.Normal;
        }

        public void NavigateTo(string path, bool pushHistory)
        {
            string norm = VirtualFileSystem.NormalizePath(path);
            if (norm == _currentPath) return;
            if (pushHistory) _history.Push(_currentPath);
            _currentPath = norm;
            _selectedEntryId = null;
            Refresh();
        }

        private void GoBack()
        {
            if (_history.Count == 0) return;
            _currentPath = _history.Pop();
            _selectedEntryId = null;
            Refresh();
        }

        private void CreateNewTextFile()
        {
            int n = 1;
            var existing = _vfs.GetEntriesInFolder(_currentPath);
            while (existing.Any(e => e.Name == $"note_{n}.txt")) n++;

            string time = PlayerState.Instance != null ? PlayerState.Instance.GetFormattedTime() : "Сейчас";
            var created = _vfs.AddFile(_currentPath, $"note_{n}.txt", VfsEntryType.TextFile,
                $"Новый текстовый файл создан в {time}.\nПуть: {_currentPath}/note_{n}.txt", time);
            _selectedEntryId = created.Id;
            Refresh();
        }

        private void OpenSelected()
        {
            if (string.IsNullOrEmpty(_selectedEntryId)) return;
            var entry = _vfs.GetEntriesInFolder(_currentPath).FirstOrDefault(e => e.Id == _selectedEntryId);
            if (entry != null) OpenEntry(entry);
        }

        private void OpenEntry(VfsEntry entry)
        {
            if (entry.IsFolder)
            {
                NavigateTo(entry.FullPath, pushHistory: true);
            }
            else
            {
                _desktop?.OpenTextViewer(entry.Name, entry.FullPath, entry.Content);
            }
        }

        private void DeleteSelected()
        {
            if (string.IsNullOrEmpty(_selectedEntryId)) return;
            if (_vfs.MoveToTrash(_selectedEntryId))
            {
                _selectedEntryId = null;
                AudioFeedbackManager.Instance?.PlayClick();
                Refresh();
            }
            else
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
            }
        }

        public void Refresh()
        {
            if (_listContent == null || _vfs == null) return;

            _pathText.text = _currentPath;
            _backBtn.interactable = _history.Count > 0;

            var entries = _vfs.GetEntriesInFolder(_currentPath);
            UITheme.ClearChildren(_listContent);

            foreach (var entry in entries)
            {
                bool selected = entry.Id == _selectedEntryId;
                var row = UITheme.CreatePanel(_listContent, "Row_" + entry.Name, selected ? UITheme.LightItemSelected : new Color(0, 0, 0, 0));
                UITheme.AddLayout(row, prefHeight: 34);

                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = row.GetComponent<Image>();
                var colors = btn.colors;
                colors.highlightedColor = UITheme.LightItemHover;
                colors.pressedColor = UITheme.LightItemSelected;
                btn.colors = colors;

                VfsEntry captured = entry;
                btn.onClick.AddListener(() =>
                {
                    float now = Time.unscaledTime;
                    if (_lastClickedId == captured.Id && (now - _lastClickTime) < 0.38f)
                    {
                        _lastClickedId = null;
                        AudioFeedbackManager.Instance?.PlayClick();
                        OpenEntry(captured);
                        return;
                    }

                    _lastClickedId = captured.Id;
                    _lastClickTime = now;
                    _selectedEntryId = captured.Id;
                    AudioFeedbackManager.Instance?.PlayClick();
                    Refresh();
                });

                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 10;
                h.padding = new RectOffset(8, 12, 3, 3);
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

                var nameTxt = UITheme.CreateText(row, "Name", entry.Name, 15f, UITheme.TextDark, TextAlignmentOptions.MidlineLeft, entry.IsFolder ? FontStyles.Bold : FontStyles.Normal);
                UITheme.AddLayout(nameTxt, flexWidth: 1);

                var sizeTxt = UITheme.CreateText(row, "Size", entry.IsFolder ? "<DIR>" : VirtualFileSystem.FormatSize(entry.SizeBytes), 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineRight, FontStyles.Normal, monospace: true);
                UITheme.AddLayout(sizeTxt, prefWidth: 90);

                var modTxt = UITheme.CreateText(row, "Mod", entry.ModifiedLabel, 13f, UITheme.TextDarkMuted, TextAlignmentOptions.MidlineRight, FontStyles.Normal, monospace: true);
                UITheme.AddLayout(modTxt, prefWidth: 90);
            }

            var selEntry = entries.FirstOrDefault(e => e.Id == _selectedEntryId);
            _openBtn.interactable = selEntry != null;
            _deleteBtn.interactable = selEntry != null && !selEntry.IsSystemProtected;
            _statusText.text = $"{entries.Count} объектов  |  Двойной клик — открыть";
        }
    }
}
