using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Домашний Дата-Центр» — Шаг 6 Core Loop + Моральный выбор Game Loop:
    /// живой парсинг сырых архивов и выбор судьбы компромата (Досье / Блог / Боссу в L.A.R.P.).
    /// </summary>
    public class DataCenterAppView : MonoBehaviour
    {
        private LarpDesktopOS _desktop;
        private TextMeshProUGUI _dcLevelText;
        private RectTransform _contentRoot;

        private readonly Dictionary<string, (Image fill, TextMeshProUGUI label)> _liveParseBars =
            new Dictionary<string, (Image, TextMeshProUGUI)>();

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

            // Шапка Дата-Центра
            var headerRow = UITheme.CreatePanel(root, "DCHeader", UITheme.BgPanel);
            UITheme.AddLayout(headerRow, prefHeight: 68);
            var hh = headerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = 16;
            hh.padding = new RectOffset(18, 16, 12, 12);
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            hh.childForceExpandHeight = true;
            hh.childForceExpandWidth = false;

            _dcLevelText = UITheme.CreateText(headerRow, "DCLvl", "ДОМАШНИЙ ДАТА-ЦЕНТР — Уровень 1 (скорость x1)", 22, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(_dcLevelText, flexWidth: 1);

            var upgBtn = UITheme.CreateButton(headerRow, "UpgBtn", "Улучшить Дата-Центр →", UITheme.AccentViolet, UITheme.TextPrimary, 17, () => _desktop.SwitchTab(LarpDesktopOS.AppTab.SideJobsShop));
            UITheme.AddLayout(upgBtn, prefWidth: 290);

            var (scroll, content) = UITheme.CreateVerticalScrollView(root, "DCScroll", UITheme.BgDesktop, 14f, 14);
            UITheme.AddLayout(scroll, flexHeight: 1);
            _contentRoot = content;

            Refresh();
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || _liveParseBars.Count == 0) return;

            foreach (var job in gm.ParseJobs)
            {
                if (!job.Completed && _liveParseBars.TryGetValue(job.ServerId, out var bar))
                {
                    float p = job.Progress;
                    float left = Mathf.Max(0f, job.Duration - job.Elapsed);
                    UITheme.SetBarFill(bar.fill, p);
                    bar.label.text = $"Парсинг сырых данных: {Mathf.RoundToInt(p * 100f)}%  (осталось ~{left:0.0} сек)";
                }
            }
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            if (gm == null || _contentRoot == null) return;

            _dcLevelText.text = $"ДОМАШНИЙ ДАТА-ЦЕНТР — Уровень {gm.DataCenterLevel}/3  (Скорость парсинга: x{gm.GetParseSpeedMultiplier():0})";

            UITheme.ClearChildren(_contentRoot);
            _liveParseBars.Clear();

            var activeJobs = gm.ParseJobs.Where(j => !j.Completed).ToList();
            var parsedServers = gm.Servers.Where(s => s.IsParsed).ToList();

            if (activeJobs.Count == 0 && parsedServers.Count == 0)
            {
                var emptyCard = UITheme.CreatePanel(_contentRoot, "EmptyCard", UITheme.BgPanel);
                UITheme.AddLayout(emptyCard, prefHeight: 200);
                var ev = emptyCard.gameObject.AddComponent<VerticalLayoutGroup>();
                ev.spacing = 14;
                ev.padding = new RectOffset(24, 24, 24, 24);
                ev.childAlignment = TextAnchor.MiddleCenter;
                ev.childControlWidth = true;
                ev.childControlHeight = true;

                var emptyTxt = UITheme.CreateText(emptyCard, "EmptyTxt",
                    "Очередь Дата-Центра пуста.\nВзломайте сервер корпорации через «Bruteforce», чтобы скачать сырые данные и начать парсинг.",
                    20, UITheme.TextMuted, TextAlignmentOptions.Center);
                UITheme.AddLayout(emptyTxt, prefHeight: 80);

                var goBrute = UITheme.CreateButton(emptyCard, "GoBrute", "Перейти к Bruteforce →", UITheme.AccentCyan, UITheme.TextDark, 18, () => _desktop.SwitchTab(LarpDesktopOS.AppTab.Bruteforce));
                UITheme.AddLayout(goBrute, prefHeight: 52, prefWidth: 320);
                return;
            }

            // 1. Активные задачи парсинга
            if (activeJobs.Count > 0)
            {
                var secHdr = UITheme.CreateText(_contentRoot, "ParsingHdr", "АКТИВНЫЙ ПАРСИНГ СЫРЫХ ДАННЫХ", 19, UITheme.AccentAmber, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(secHdr, prefHeight: 30);

                foreach (var job in activeJobs)
                {
                    var card = UITheme.CreatePanel(_contentRoot, "Job_" + job.ServerId, UITheme.BgCard);
                    UITheme.AddLayout(card, prefHeight: 102);
                    var cv = card.gameObject.AddComponent<VerticalLayoutGroup>();
                    cv.spacing = 10;
                    cv.padding = new RectOffset(16, 16, 12, 12);
                    cv.childControlWidth = true;
                    cv.childControlHeight = true;

                    var t = UITheme.CreateText(card, "Name", $"[ОБРАБОТКА] {job.ServerName}", 19, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    UITheme.AddLayout(t, prefHeight: 28);

                    var (bRoot, bFill, bLbl) = UITheme.CreateProgressBar(card, "Bar", UITheme.BgDesktop, UITheme.AccentAmber, "Парсинг...", 17);
                    UITheme.AddLayout(bRoot, prefHeight: 38);
                    UITheme.SetBarFill(bFill, job.Progress);

                    _liveParseBars[job.ServerId] = (bFill, bLbl);
                }
            }

            // 2. Распарсенный компромат + Моральный выбор
            if (parsedServers.Count > 0)
            {
                var secHdr2 = UITheme.CreateText(_contentRoot, "ReadyHdr", "РАСПАРСЕННЫЙ КОМПРОМАТ — ТОЧКА ВЫБОРА", 19, UITheme.AccentGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(secHdr2, prefHeight: 32);

                foreach (var srv in parsedServers)
                {
                    var card = UITheme.CreatePanel(_contentRoot, "Ev_" + srv.Id, srv.IsDecided ? UITheme.BgPanel : UITheme.BgCard);
                    UITheme.AddLayout(card, prefHeight: srv.IsDecided ? 170 : 250);
                    var cv = card.gameObject.AddComponent<VerticalLayoutGroup>();
                    cv.spacing = 10;
                    cv.padding = new RectOffset(18, 18, 14, 14);
                    cv.childControlWidth = true;
                    cv.childControlHeight = true;
                    cv.childForceExpandWidth = true;
                    cv.childForceExpandHeight = false;

                    var title = UITheme.CreateText(card, "EvTitle", srv.EvidenceTitle, 21, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    UITheme.AddLayout(title, prefHeight: 32);

                    var desc = UITheme.CreateText(card, "EvDesc", srv.EvidenceSummary, 18, UITheme.TextPrimary);
                    UITheme.AddLayout(desc, prefHeight: 72);

                    if (!srv.IsDecided)
                    {
                        var prompt = UITheme.CreateText(card, "Prompt", "Выберите необратимое действие с этим архивом:", 16, UITheme.AccentAmber, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                        UITheme.AddLayout(prompt, prefHeight: 24);

                        var btnRow = UITheme.CreatePanel(card, "ChoiceRow", new Color(0, 0, 0, 0));
                        UITheme.AddLayout(btnRow, prefHeight: 72);
                        var bh = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                        bh.spacing = 12;
                        bh.childControlWidth = true;
                        bh.childControlHeight = true;
                        bh.childForceExpandWidth = true;
                        bh.childForceExpandHeight = true;

                        string sId = srv.Id;

                        UITheme.CreateButton(
                            btnRow,
                            "BtnDossier",
                            "В ДОСЬЕ (Мие)\n+25 Досье (Суд)",
                            UITheme.AccentCyan,
                            UITheme.TextDark,
                            16,
                            () => { gm.ApplyMoralChoice(sId, MoralBranch.Dossier); Refresh(); });

                        UITheme.CreateButton(
                            btnRow,
                            "BtnBlog",
                            "В БЛОГ (Призраку)\n+25 Фолловеры | +$90 | +5% Trace",
                            UITheme.AccentViolet,
                            UITheme.TextPrimary,
                            16,
                            () => { gm.ApplyMoralChoice(sId, MoralBranch.Blog); Refresh(); });

                        UITheme.CreateButton(
                            btnRow,
                            "BtnBoss",
                            "СДАТЬ БОССУ (Маркусу)\n+25 Авторитет | +$110 | -15% Trace",
                            UITheme.AccentAmber,
                            UITheme.TextDark,
                            16,
                            () => { gm.ApplyMoralChoice(sId, MoralBranch.Boss); Refresh(); });
                    }
                    else
                    {
                        string branchTxt = srv.ChosenBranch switch
                        {
                            MoralBranch.Dossier => "<color=#00D4FF>[OK] РЕШЕНИЕ ПРИНЯТО: Передано Мие в ДОСЬЕ для суда.</color>",
                            MoralBranch.Blog => "<color=#9D6BFF>[OK] РЕШЕНИЕ ПРИНЯТО: Опубликовано в анонимном БЛОГЕ (удар по акциям L.A.R.P.).</color>",
                            MoralBranch.Boss => "<color=#FFB84D>[OK] РЕШЕНИЕ ПРИНЯТО: Уязвимость сдана шефу СБ Маркусу Стоуну.</color>",
                            _ => ""
                        };
                        var done = UITheme.CreateText(card, "DoneChoice", branchTxt, 18, UITheme.AccentGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                        UITheme.AddLayout(done, prefHeight: 34);
                    }
                }
            }
        }
    }
}
