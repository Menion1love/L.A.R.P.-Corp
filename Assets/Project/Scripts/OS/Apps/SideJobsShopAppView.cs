using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Заказы и DarkNet» — Game Loop (Слайд 20):
    /// побочный заработок (фриланс + 3D-принтер) и апгрейды убежища (Дата-Центр, 3D-принтер, чистка логов, кофе).
    /// </summary>
    public class SideJobsShopAppView : MonoBehaviour
    {
        private RectTransform _jobsContent;
        private RectTransform _shopContent;
        private TextMeshProUGUI _balanceHeader;

        public void Initialize(LarpDesktopOS desktop, RectTransform root)
        {
            var outerV = root.gameObject.AddComponent<VerticalLayoutGroup>();
            outerV.spacing = 12;
            outerV.padding = new RectOffset(16, 16, 14, 14);
            outerV.childControlWidth = true;
            outerV.childControlHeight = true;
            outerV.childForceExpandWidth = true;
            outerV.childForceExpandHeight = false;

            _balanceHeader = UITheme.CreateText(root, "BalHdr", "ЭКОНОМИКА И АПГРЕЙДЫ УБЕЖИЩА", 22, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(_balanceHeader, prefHeight: 36);

            var cols = UITheme.CreatePanel(root, "Cols", new Color(0, 0, 0, 0));
            UITheme.AddLayout(cols, flexHeight: 1);
            var h = cols.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            // Левая колонка: Побочные пути заработка
            var leftPanel = UITheme.CreatePanel(cols, "JobsPanel", UITheme.BgPanel);
            var lv = leftPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 10;
            lv.padding = new RectOffset(14, 14, 12, 12);
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;

            var jTitle = UITheme.CreateText(leftPanel, "JT", "ПОБОЧНЫЕ ЗАКАЗЫ (ФРИЛАНС / 3D-ПЕЧАТЬ)", 19, UITheme.AccentGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(jTitle, prefHeight: 32);

            var (jScroll, jContent) = UITheme.CreateVerticalScrollView(leftPanel, "JobsScroll", UITheme.BgDesktop, 10, 10);
            UITheme.AddLayout(jScroll, flexHeight: 1);
            _jobsContent = jContent;

            // Правая колонка: DarkNet Магазин & Апгрейды
            var rightPanel = UITheme.CreatePanel(cols, "ShopPanel", UITheme.BgPanel);
            var rv = rightPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 10;
            rv.padding = new RectOffset(14, 14, 12, 12);
            rv.childControlWidth = true;
            rv.childControlHeight = true;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;

            var sTitle = UITheme.CreateText(rightPanel, "ST", "DARKNET-МАГАЗИН И АПГРЕЙД ДАТА-ЦЕНТРА", 19, UITheme.AccentViolet, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(sTitle, prefHeight: 32);

            var (sScroll, sContent) = UITheme.CreateVerticalScrollView(rightPanel, "ShopScroll", UITheme.BgDesktop, 10, 10);
            UITheme.AddLayout(sScroll, flexHeight: 1);
            _shopContent = sContent;

            Refresh();
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            if (gm == null || _jobsContent == null || _shopContent == null) return;

            _balanceHeader.text =
                $"БАЛАНС: <color=#2EE676>${gm.Money}</color>   |   Дата-Центр: Ур.{gm.DataCenterLevel}   |   3D-Принтер: {(gm.HasPrinter3D ? "<color=#2EE676>УСТАНОВЛЕН</color>" : "<color=#FFB84D>НЕТ</color>")}   |   Усталость: {gm.Fatigue}%";

            // 1. Заказы
            UITheme.ClearChildren(_jobsContent);
            foreach (var job in gm.FreelanceJobs.Where(j => j.UnlockDay <= gm.Day))
            {
                var card = UITheme.CreatePanel(_jobsContent, "Job_" + job.Id, job.IsCompleted ? UITheme.BgPanel : UITheme.BgCard);
                UITheme.AddLayout(card, prefHeight: 148);
                var cv = card.gameObject.AddComponent<VerticalLayoutGroup>();
                cv.spacing = 6;
                cv.padding = new RectOffset(14, 14, 10, 10);
                cv.childControlWidth = true;
                cv.childControlHeight = true;
                cv.childForceExpandWidth = true;
                cv.childForceExpandHeight = false;

                string printerTag = job.RequiresPrinter3D ? " [3D-ПРИНТЕР]" : "";
                var t1 = UITheme.CreateText(card, "T1", $"{job.Title}{printerTag}", 18, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(t1, prefHeight: 26);

                var t2 = UITheme.CreateText(card, "T2", $"{job.Client}: {job.Description}", 15, UITheme.TextMuted);
                UITheme.AddLayout(t2, prefHeight: 44);

                if (job.IsCompleted)
                {
                    var done = UITheme.CreateText(card, "Done", "<color=#2EE676>[OK] Заказ выполнен и оплачен</color>", 16, UITheme.AccentGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    UITheme.AddLayout(done, prefHeight: 38);
                }
                else
                {
                    bool locked = job.RequiresPrinter3D && !gm.HasPrinter3D;
                    string jId = job.Id;
                    var btn = UITheme.CreateButton(
                        card,
                        "DoJobBtn",
                        locked
                            ? "Требуется 3D-Принтер (купите справа)"
                            : $"Выполнить заказ (+${job.RewardMoney} | Усталость +{job.FatigueCost}%)",
                        locked ? UITheme.BgInput : UITheme.AccentGreen,
                        locked ? UITheme.TextMuted : UITheme.TextDark,
                        16,
                        () =>
                        {
                            if (!gm.CompleteFreelanceJob(jId, out string reason))
                            {
                                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                                gm.PostToast(reason);
                            }
                            Refresh();
                        });
                    btn.interactable = !locked;
                    UITheme.AddLayout(btn, prefHeight: 44);
                }
            }

            // 2. Магазин DarkNet
            UITheme.ClearChildren(_shopContent);
            foreach (var item in gm.ShopItems)
            {
                var card = UITheme.CreatePanel(_shopContent, "Shop_" + item.Id, item.Purchased ? UITheme.BgPanel : UITheme.BgCard);
                UITheme.AddLayout(card, prefHeight: 146);
                var cv = card.gameObject.AddComponent<VerticalLayoutGroup>();
                cv.spacing = 6;
                cv.padding = new RectOffset(14, 14, 10, 10);
                cv.childControlWidth = true;
                cv.childControlHeight = true;
                cv.childForceExpandWidth = true;
                cv.childForceExpandHeight = false;

                var t1 = UITheme.CreateText(card, "T1", $"{item.Title}  —  <color=#FFB84D>${item.Cost}</color>", 18, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(t1, prefHeight: 26);

                var t2 = UITheme.CreateText(card, "T2", item.Description, 15, UITheme.TextMuted);
                UITheme.AddLayout(t2, prefHeight: 44);

                if (!item.Repeatable && item.Purchased)
                {
                    var owned = UITheme.CreateText(card, "Owned", "<color=#2EE676>[OK] Установлено в убежище</color>", 16, UITheme.AccentGreen, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                    UITheme.AddLayout(owned, prefHeight: 38);
                }
                else
                {
                    bool canAfford = gm.Money >= item.Cost;
                    string iId = item.Id;
                    var buyBtn = UITheme.CreateButton(
                        card,
                        "BuyBtn",
                        canAfford ? $"Купить за ${item.Cost}" : $"Не хватает средств (${item.Cost})",
                        canAfford ? UITheme.AccentViolet : UITheme.BgInput,
                        canAfford ? UITheme.TextPrimary : UITheme.TextMuted,
                        16,
                        () =>
                        {
                            if (!gm.PurchaseShopItem(iId, out string reason))
                            {
                                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                                gm.PostToast(reason);
                            }
                            Refresh();
                        });
                    UITheme.AddLayout(buyBtn, prefHeight: 44);
                }
            }
        }
    }
}
