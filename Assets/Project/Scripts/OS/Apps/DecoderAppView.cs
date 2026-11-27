using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Декодер и Сборщик ключа» — Шаги 2 и 3 Core Loop:
    /// сборка пронумерованных частей ключа и декодирование зашифрованных фрагментов (Base64, HEX, ROT13).
    /// </summary>
    public class DecoderAppView : MonoBehaviour
    {
        private RectTransform _serverTabsRow;
        private RectTransform _slotsRow;
        private TextMeshProUGUI _assembledKeyText;
        private Button _goBruteforceBtn;
        private RectTransform _decoderWorkArea;
        private TextMeshProUGUI _decoderOutputText;

        private string _selectedServerId = "srv_eye_01";
        private string _selectedFragmentId;
        private LarpDesktopOS _desktop;

        public void Initialize(LarpDesktopOS desktop, RectTransform root)
        {
            _desktop = desktop;

            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14;
            v.padding = new RectOffset(18, 18, 16, 16);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var title = UITheme.CreateText(root, "Title", "СБОРЩИК КЛЮЧЕЙ И ПРОГРАММА-ДЕКОДЕР", 24, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(title, prefHeight: 36);

            // Выбор целевого сервера
            _serverTabsRow = UITheme.CreatePanel(root, "ServerTabs", new Color(0, 0, 0, 0));
            UITheme.AddLayout(_serverTabsRow, prefHeight: 50);
            var stH = _serverTabsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            stH.spacing = 12;
            stH.childControlWidth = true;
            stH.childControlHeight = true;
            stH.childForceExpandHeight = true;
            stH.childForceExpandWidth = true;

            // 3 слота ключа
            _slotsRow = UITheme.CreatePanel(root, "KeySlots", new Color(0, 0, 0, 0));
            UITheme.AddLayout(_slotsRow, prefHeight: 175);
            var slH = _slotsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            slH.spacing = 16;
            slH.childControlWidth = true;
            slH.childControlHeight = true;
            slH.childForceExpandHeight = true;
            slH.childForceExpandWidth = true;

            // Итоговый собранный ключ
            var resultRow = UITheme.CreatePanel(root, "AssembledRow", UITheme.BgPanel);
            UITheme.AddLayout(resultRow, prefHeight: 74);
            var rH = resultRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rH.spacing = 16;
            rH.padding = new RectOffset(20, 16, 12, 12);
            rH.childControlWidth = true;
            rH.childControlHeight = true;
            rH.childForceExpandHeight = true;
            rH.childForceExpandWidth = false;

            _assembledKeyText = UITheme.CreateText(resultRow, "AssembledKey", "СОБРАННЫЙ КЛЮЧ: ????-????-????", 24, UITheme.AccentAmber, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(_assembledKeyText, flexWidth: 1);

            _goBruteforceBtn = UITheme.CreateButton(
                resultRow,
                "GoBruteBtn",
                "Передать ключ в Bruteforce →",
                UITheme.AccentGreen,
                UITheme.TextDark,
                19,
                () => _desktop.OpenBruteforceForServer(_selectedServerId));
            UITheme.AddLayout(_goBruteforceBtn, prefWidth: 360);

            // Панель декодера
            _decoderWorkArea = UITheme.CreatePanel(root, "DecoderArea", UITheme.BgPanel);
            UITheme.AddLayout(_decoderWorkArea, flexHeight: 1);
            var dv = _decoderWorkArea.gameObject.AddComponent<VerticalLayoutGroup>();
            dv.spacing = 14;
            dv.padding = new RectOffset(20, 20, 18, 18);
            dv.childControlWidth = true;
            dv.childControlHeight = true;
            dv.childForceExpandWidth = true;
            dv.childForceExpandHeight = false;

            Refresh();
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            if (gm == null || _serverTabsRow == null) return;

            var visibleServers = gm.Servers.Where(s => s.UnlockDay <= gm.Day).ToList();
            if (visibleServers.All(s => s.Id != _selectedServerId))
                _selectedServerId = visibleServers.FirstOrDefault()?.Id;

            // 1. Вкладки серверов
            UITheme.ClearChildren(_serverTabsRow);
            foreach (var srv in visibleServers)
            {
                bool active = srv.Id == _selectedServerId;
                string mark = srv.IsHacked ? " [ВЗЛОМАН]" : (gm.IsServerKeyComplete(srv.Id) ? " [КЛЮЧ ГОТОВ]" : "");
                string idCopy = srv.Id;
                UITheme.CreateButton(
                    _serverTabsRow,
                    "SrvTab_" + srv.Id,
                    srv.DisplayName + mark,
                    active ? UITheme.AccentCyan : UITheme.BgCard,
                    active ? UITheme.TextDark : UITheme.TextPrimary,
                    16,
                    () =>
                    {
                        _selectedServerId = idCopy;
                        _selectedFragmentId = null;
                        Refresh();
                    });
            }

            // 2. Слоты фрагментов выбранного сервера
            var frags = gm.GetFragmentsForServer(_selectedServerId);
            if (string.IsNullOrEmpty(_selectedFragmentId) || frags.All(f => f.Id != _selectedFragmentId))
            {
                _selectedFragmentId = frags.FirstOrDefault(f => f.IsFound && !f.IsDecoded)?.Id ?? frags.FirstOrDefault()?.Id;
            }

            UITheme.ClearChildren(_slotsRow);
            foreach (var frag in frags)
            {
                bool isSel = frag.Id == _selectedFragmentId;
                Color borderBg = !frag.IsFound
                    ? UITheme.BgDesktop
                    : (frag.IsDecoded ? new Color(0.08f, 0.24f, 0.16f, 1f) : (isSel ? UITheme.BgCardHover : UITheme.BgCard));

                var slotCard = UITheme.CreatePanel(_slotsRow, "Slot_" + frag.SlotIndex, borderBg);
                var btn = slotCard.gameObject.AddComponent<Button>();
                btn.targetGraphic = slotCard.GetComponent<Image>();
                string fId = frag.Id;
                btn.onClick.AddListener(() =>
                {
                    AudioFeedbackManager.Instance?.PlayClick();
                    _selectedFragmentId = fId;
                    Refresh();
                });

                var sv = slotCard.gameObject.AddComponent<VerticalLayoutGroup>();
                sv.spacing = 8;
                sv.padding = new RectOffset(16, 16, 14, 14);
                sv.childControlWidth = true;
                sv.childControlHeight = true;

                string stateTxt = !frag.IsFound
                    ? "<color=#FF4B5C>НЕ НАЙДЕН</color>"
                    : (frag.IsDecoded ? "<color=#2EE676>ГОТОВ К РАБОТЕ</color>" : "<color=#FFB84D>ТРЕБУЕТ ДЕКОДИРОВАНИЯ</color>");

                var h1 = UITheme.CreateText(slotCard, "H1", $"ЧАСТЬ КЛЮЧА #{frag.SlotIndex}  |  {stateTxt}", 16, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(h1, prefHeight: 28);

                string valStr = !frag.IsFound
                    ? "Ищите в Браузере:\n" + frag.SourceUrl
                    : (frag.IsDecoded ? $"Значение: <b>{frag.DecodedValue}</b>" : $"Сырой код: <color=#FFB84D><b>{frag.RawValue}</b></color>\nКликните, чтобы декодировать ниже");

                var h2 = UITheme.CreateText(slotCard, "H2", valStr, 18, frag.IsFound ? UITheme.TextPrimary : UITheme.TextMuted);
                UITheme.AddLayout(h2, flexHeight: 1);
            }

            // 3. Собранный ключ
            bool complete = gm.IsServerKeyComplete(_selectedServerId);
            string assembled = gm.GetAssembledKey(_selectedServerId);
            _assembledKeyText.text = $"СОБРАННЫЙ КЛЮЧ:  {assembled}";
            _assembledKeyText.color = complete ? UITheme.AccentGreen : UITheme.AccentAmber;
            _goBruteforceBtn.interactable = complete;

            // 4. Рабочая зона декодера
            UITheme.ClearChildren(_decoderWorkArea);
            var activeFrag = frags.FirstOrDefault(f => f.Id == _selectedFragmentId);

            var decHeader = UITheme.CreateText(_decoderWorkArea, "DecHdr", "ДЕКОДЕР ФРАГМЕНТОВ (выберите алгоритм расшифровки)", 20, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(decHeader, prefHeight: 32);

            if (activeFrag == null || !activeFrag.IsFound)
            {
                var notFound = UITheme.CreateText(
                    _decoderWorkArea,
                    "NF",
                    activeFrag == null
                        ? "Выберите сервер выше."
                        : $"Фрагмент #{activeFrag.SlotIndex} ещё не найден.\nПерейдите в Браузер на страницу: <b>{activeFrag.SourceUrl}</b>\nПодсказка: {activeFrag.Hint}",
                    19,
                    UITheme.TextMuted);
                UITheme.AddLayout(notFound, prefHeight: 100);

                if (activeFrag != null)
                {
                    string u = activeFrag.SourceUrl;
                    var openPageBtn = UITheme.CreateButton(
                        _decoderWorkArea,
                        "OpenPageBtn",
                        $"Перейти в Браузер → {u}",
                        UITheme.AccentCyan,
                        UITheme.TextDark,
                        18,
                        () => _desktop.OpenBrowserAtUrl(u));
                    UITheme.AddLayout(openPageBtn, prefHeight: 52, prefWidth: 480);
                }
                return;
            }

            if (activeFrag.IsDecoded)
            {
                var doneInfo = UITheme.CreateText(
                    _decoderWorkArea,
                    "DoneInfo",
                    $"<color=#2EE676>[OK] Фрагмент #{activeFrag.SlotIndex} уже расшифрован!</color>\nИсходная строка: {activeFrag.RawValue}  →  Результат: <b>{activeFrag.DecodedValue}</b>",
                    21,
                    UITheme.TextPrimary);
                UITheme.AddLayout(doneInfo, prefHeight: 80);
                return;
            }

            var fragInfo = UITheme.CreateText(
                _decoderWorkArea,
                "FragInfo",
                $"Входная строка (Часть #{activeFrag.SlotIndex}):  <color=#FFB84D><b>{activeFrag.RawValue}</b></color>\nПодсказка инспекции: {activeFrag.Hint}",
                20,
                UITheme.TextPrimary);
            UITheme.AddLayout(fragInfo, prefHeight: 68);

            var btnRow = UITheme.CreatePanel(_decoderWorkArea, "AlgoRow", new Color(0, 0, 0, 0));
            UITheme.AddLayout(btnRow, prefHeight: 62);
            var bh = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 14;
            bh.childControlWidth = true;
            bh.childControlHeight = true;
            bh.childForceExpandWidth = true;
            bh.childForceExpandHeight = true;

            _decoderOutputText = UITheme.CreateText(_decoderWorkArea, "DecOut", "Нажмите на подходящий декодер выше...", 19, UITheme.TextMuted);
            UITheme.AddLayout(_decoderOutputText, prefHeight: 46);

            void AddAlgoBtn(KeyEncoding enc, string titleStr, Color col)
            {
                string fId = activeFrag.Id;
                UITheme.CreateButton(btnRow, "Algo_" + enc, titleStr, col, UITheme.TextDark, 18, () =>
                {
                    bool ok = gm.TryDecodeFragment(fId, enc, out string decodedOut);
                    if (ok)
                    {
                        Refresh();
                    }
                    else if (_decoderOutputText != null)
                    {
                        _decoderOutputText.color = UITheme.AccentRed;
                        _decoderOutputText.text = $"Результат {enc}: «{decodedOut}» — не подходит! Попробуйте другой формат.";
                    }
                });
            }

            AddAlgoBtn(KeyEncoding.Base64, "Декодировать: BASE64", UITheme.AccentCyan);
            AddAlgoBtn(KeyEncoding.Hex, "Декодировать: HEX (16-ричный)", UITheme.AccentAmber);
            AddAlgoBtn(KeyEncoding.Rot13, "Декодировать: ROT13 (Цезарь)", UITheme.AccentViolet);
        }
    }
}
