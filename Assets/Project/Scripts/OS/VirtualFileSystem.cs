using System;
using System.Collections.Generic;
using System.Linq;

namespace Project.LinuxOS
{
    public enum VfsEntryType
    {
        Folder,
        TextFile,
        LogFile,
        ArchiveFile
    }

    [Serializable]
    public class VfsEntry
    {
        public string Id;
        public string Name;
        public string FolderPath;         // e.g. "/home/nullptr/Documents"
        public string OriginalFolderPath; // используется для восстановления из Trash
        public VfsEntryType EntryType;
        public string Content;
        public int SizeBytes;
        public string ModifiedLabel;
        public bool IsSystemProtected;

        public string FullPath => FolderPath == "/" ? "/" + Name : FolderPath.TrimEnd('/') + "/" + Name;
        public bool IsFolder => EntryType == VfsEntryType.Folder;
    }

    /// <summary>
    /// Виртуальная файловая система Linux для проводника (My Computer), корзины (Trash) и просмотра файлов.
    /// </summary>
    public class VirtualFileSystem
    {
        public const string HomePath = "/home/nullptr";
        public const string DesktopPath = "/home/nullptr/Desktop";
        public const string DocumentsPath = "/home/nullptr/Documents";
        public const string DownloadsPath = "/home/nullptr/Downloads";
        public const string LogsPath = "/home/nullptr/Logs";
        public const string TrashPath = "/home/nullptr/.trash";

        private readonly List<VfsEntry> _entries = new List<VfsEntry>();
        public event Action OnFileSystemChanged;

        public VirtualFileSystem()
        {
            SeedDefaultFiles();
        }

        private void SeedDefaultFiles()
        {
            _entries.Clear();

            // Папки в /home/nullptr
            AddFolder(HomePath, "Desktop", true);
            AddFolder(HomePath, "Documents", true);
            AddFolder(HomePath, "Downloads", true);
            AddFolder(HomePath, "Logs", true);

            // Файлы на рабочем столе / в документах
            AddFile(DocumentsPath, "shift_checklist.txt", VfsEntryType.TextFile,
                "Памятка младшего аналитика L.A.R.P. Corp.\n" +
                "------------------------------------------\n" +
                "1. Проверять входящие тикеты Маркуса Стоуна до утра.\n" +
                "2. Не подключаться к корпоративным узлам без чистой сессии.\n" +
                "3. Если Wi-Fi отвалился — проверить доступную открытую точку в правом верхнем углу.\n" +
                "4. Важные заметки и найденные ключи удобно записывать в Notes.",
                "22:00");

            AddFile(DocumentsPath, "network_memo.txt", VfsEntryType.TextFile,
                "Локальная сеть дома:\n" +
                "Соседских роутеров вокруг больше десятка, все под WPA/WPA2.\n" +
                "Пока нет утилит подбора паролей, используем единственную открытую точку в списке Wi-Fi.",
                "21:45");

            AddFile(DocumentsPath, "larp_structure.txt", VfsEntryType.TextFile,
                "Отделы L.A.R.P. Corp.:\n" +
                "- Security & Audit (рук. Маркус Стоун)\n" +
                "- Telemetry / Eye Monitor (10.42.8.14)\n" +
                "- Internal Finance (10.42.19.88)\n" +
                "- Board of Directors (10.42.99.201)",
                "Вчера");

            AddFile(LogsPath, "boot.log", VfsEntryType.LogFile,
                "[  OK  ] Started L.A.R.P. Workstation Linux 6.6-lts.\n" +
                "[  OK  ] Mounted /home/nullptr (ext4).\n" +
                "[  OK  ] Started NetworkManager wlan0 interface.\n" +
                "[ INFO ] Home Data-Center daemon listening on 127.0.0.1:9443.",
                "22:00");

            AddFile(LogsPath, "auth.log", VfsEntryType.LogFile,
                "22:00:02 nullptr login[412]: pam_unix(login:session): session opened for user nullptr\n" +
                "22:00:04 nullptr sshd[519]: Server listening on 0.0.0.0 port 22.",
                "22:00");

            AddFile(DownloadsPath, "readme_downloads.txt", VfsEntryType.TextFile,
                "Сюда сохраняются скачанные и распарсенные архивы из Домашнего Дата-Центра.",
                "22:00");

            // Атмосферные удалённые файлы в Корзине (Trash)
            var t1 = AddFile(TrashPath, "draft_resignation.txt", VfsEntryType.TextFile,
                "Кому: Отдел кадров L.A.R.P. Corp.\n\n" +
                "Прошу уволить меня по собственному желанию...\n" +
                "[черновик удалён — слишком рано уходить, сначала нужно разобраться с архивом]",
                "Вчера");
            t1.OriginalFolderPath = DocumentsPath;

            var t2 = AddFile(TrashPath, "temp_cache_04.log", VfsEntryType.LogFile,
                "CACHE_DUMP 0x004F12A\nstatus=EXPIRED\nsrc=intranet.larp.corp",
                "Вчера");
            t2.OriginalFolderPath = LogsPath;
        }

        public VfsEntry AddFolder(string parentPath, string name, bool isProtected = false)
        {
            var entry = new VfsEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                FolderPath = NormalizePath(parentPath),
                OriginalFolderPath = NormalizePath(parentPath),
                EntryType = VfsEntryType.Folder,
                Content = string.Empty,
                SizeBytes = 0,
                ModifiedLabel = "Сегодня",
                IsSystemProtected = isProtected
            };
            _entries.Add(entry);
            OnFileSystemChanged?.Invoke();
            return entry;
        }

        public VfsEntry AddFile(string folderPath, string name, VfsEntryType type, string content, string modified = "Сейчас")
        {
            string normFolder = NormalizePath(folderPath);
            var existing = _entries.FirstOrDefault(e => e.FolderPath == normFolder && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Content = content ?? string.Empty;
                existing.SizeBytes = System.Text.Encoding.UTF8.GetByteCount(existing.Content);
                existing.ModifiedLabel = modified;
                OnFileSystemChanged?.Invoke();
                return existing;
            }

            var entry = new VfsEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                FolderPath = normFolder,
                OriginalFolderPath = normFolder,
                EntryType = type,
                Content = content ?? string.Empty,
                SizeBytes = System.Text.Encoding.UTF8.GetByteCount(content ?? string.Empty),
                ModifiedLabel = modified,
                IsSystemProtected = false
            };
            _entries.Add(entry);
            OnFileSystemChanged?.Invoke();
            return entry;
        }

        public List<VfsEntry> GetEntriesInFolder(string folderPath)
        {
            string norm = NormalizePath(folderPath);
            return _entries
                .Where(e => e.FolderPath == norm)
                .OrderByDescending(e => e.IsFolder)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<VfsEntry> GetTrashEntries()
        {
            return GetEntriesInFolder(TrashPath);
        }

        public int TrashCount => _entries.Count(e => e.FolderPath == TrashPath);

        public bool MoveToTrash(string entryId)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null || entry.IsSystemProtected || entry.FolderPath == TrashPath)
                return false;

            entry.OriginalFolderPath = entry.FolderPath;
            entry.FolderPath = TrashPath;
            OnFileSystemChanged?.Invoke();
            return true;
        }

        public bool RestoreFromTrash(string entryId)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId && e.FolderPath == TrashPath);
            if (entry == null)
                return false;

            entry.FolderPath = string.IsNullOrEmpty(entry.OriginalFolderPath) ? DocumentsPath : entry.OriginalFolderPath;
            OnFileSystemChanged?.Invoke();
            return true;
        }

        public bool DeletePermanently(string entryId)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null || entry.IsSystemProtected)
                return false;

            _entries.Remove(entry);
            OnFileSystemChanged?.Invoke();
            return true;
        }

        public void EmptyTrash()
        {
            int removed = _entries.RemoveAll(e => e.FolderPath == TrashPath);
            if (removed > 0)
                OnFileSystemChanged?.Invoke();
        }

        public static string GetParentPath(string path)
        {
            string norm = NormalizePath(path);
            if (norm == HomePath || norm == "/" || norm == TrashPath)
                return HomePath;

            int idx = norm.LastIndexOf('/');
            if (idx <= 0) return HomePath;
            string parent = norm.Substring(0, idx);
            return parent.Length < HomePath.Length ? HomePath : parent;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return HomePath;
            string p = path.Trim();
            if (p.Length > 1 && p.EndsWith("/")) p = p.TrimEnd('/');
            return p;
        }

        public static string FormatSize(int bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            return $"{bytes / 1024f:0.0} KB";
        }
    }
}
