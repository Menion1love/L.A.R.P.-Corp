using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    public class MailAppView : MonoBehaviour
    {
        private RectTransform _listContent;
        private TextMeshProUGUI _senderText;
        private TextMeshProUGUI _subjectText;
        private TextMeshProUGUI _bodyText;
        private RectTransform _actionsContainer;
        private string _selectedMailId;
        private LarpDesktopOS _desktop;

        public void Initialize(LarpDesktopOS desktop, RectTransform root)
        {
            _desktop = desktop;

            var hlg = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 16;
            hlg.padding = new RectOffset(16, 16, 16, 16);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;

            // Левая колонка: список писем
            var leftPanel = UITheme.CreatePanel(root, "MailListPanel", UITheme.BgPanel);
            UITheme.AddLayout(leftPanel, prefWidth: 340);
            var leftVlg = leftPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            leftVlg.spacing = 10;
            leftVlg.padding = new RectOffset(14, 14, 14, 14);
            leftVlg.childControlWidth = true;
            leftVlg.childControlHeight = true;
            leftVlg.childForceExpandWidth = true;
            leftVlg.childForceExpandHeight = false;

            var header = UITheme.CreateText(leftPanel, "Header", "ВХОДЯЩИЕ И МЕССЕНДЖЕР", 22, UITheme.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(header, prefHeight: 36);

            var (scroll, content) = UITheme.CreateVerticalScrollView(leftPanel, "MailScroll", UITheme.BgDesktop, 8f, 8);
            UITheme.AddLayout(scroll, flexHeight: 1);
            _listContent = content;

            // Правая колонка: просмотр письма
            var rightPanel = UITheme.CreatePanel(root, "MailReaderPanel", UITheme.BgPanel);
            UITheme.AddLayout(rightPanel, flexWidth: 1);
            var rightVlg = rightPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            rightVlg.spacing = 14;
            rightVlg.padding = new RectOffset(22, 22, 20, 20);
            rightVlg.childControlWidth = true;
            rightVlg.childControlHeight = true;
            rightVlg.childForceExpandWidth = true;
            rightVlg.childForceExpandHeight = false;

            _subjectText = UITheme.CreateText(rightPanel, "Subject", "Выберите сообщение", 26, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(_subjectText, prefHeight: 42);

            _senderText = UITheme.CreateText(rightPanel, "Sender", "", 18, UITheme.AccentCyan);
            UITheme.AddLayout(_senderText, prefHeight: 28);

            var divider = UITheme.CreatePanel(rightPanel, "Divider", new Color(1, 1, 1, 0.12f));
            UITheme.AddLayout(divider, prefHeight: 2);

            var bodyCard = UITheme.CreatePanel(rightPanel, "BodyCard", UITheme.BgDesktop);
            UITheme.AddLayout(bodyCard, flexHeight: 1);
            _bodyText = UITheme.CreateText(bodyCard, "BodyText", "", 20, UITheme.TextPrimary, TextAlignmentOptions.TopLeft);
            UITheme.StretchFill(_bodyText.rectTransform, 18, 18, 18, 18);

            _actionsContainer = UITheme.CreatePanel(rightPanel, "Actions", new Color(0, 0, 0, 0));
            UITheme.AddLayout(_actionsContainer, prefHeight: 62);
            var actHlg = _actionsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            actHlg.spacing = 12;
            actHlg.childControlWidth = true;
            actHlg.childControlHeight = true;
            actHlg.childForceExpandHeight = true;
            actHlg.childForceExpandWidth = false;

            Refresh();
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            if (gm == null || _listContent == null) return;

            var visibleMail = gm.Mail.Where(m => m.DayReceived <= gm.Day).ToList();
            if (string.IsNullOrEmpty(_selectedMailId) || visibleMail.All(m => m.Id != _selectedMailId))
            {
                _selectedMailId = visibleMail.FirstOrDefault()?.Id;
            }

            UITheme.ClearChildren(_listContent);
            foreach (var msg in visibleMail)
            {
                bool selected = msg.Id == _selectedMailId;
                Color bg = selected ? UITheme.BgCardHover : UITheme.BgCard;
                var card = UITheme.CreatePanel(_listContent, "Mail_" + msg.Id, bg);
                UITheme.AddLayout(card, prefHeight: 92);

                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card.GetComponent<Image>();
                string idCopy = msg.Id;
                btn.onClick.AddListener(() =>
                {
                    AudioFeedbackManager.Instance?.PlayClick();
                    _selectedMailId = idCopy;
                    gm.MarkMailRead(idCopy);
                    Refresh();
                });

                var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(14, 14, 10, 10);
                vlg.spacing = 4;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;

                string badge = !msg.IsRead ? "[НОВОЕ] " : (msg.HasTicketAction && !msg.TicketCompleted ? "[ТИКЕТ] " : "");
                Color titleCol = !msg.IsRead ? UITheme.AccentAmber : UITheme.TextPrimary;

                var t1 = UITheme.CreateText(card, "Sender", $"{badge}{msg.SenderName}", 18, titleCol, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UITheme.AddLayout(t1, prefHeight: 26);

                var t2 = UITheme.CreateText(card, "Subject", msg.Subject, 16, UITheme.TextMuted);
                UITheme.AddLayout(t2, prefHeight: 40);
            }

            // Отображаем выбранное письмо
            var active = visibleMail.FirstOrDefault(m => m.Id == _selectedMailId);
            UITheme.ClearChildren(_actionsContainer);

            if (active == null)
            {
                _subjectText.text = "Нет сообщений";
                _senderText.text = "";
                _bodyText.text = "";
                return;
            }

            gm.MarkMailRead(active.Id);
            _subjectText.text = active.Subject;
            _senderText.text = $"От: {active.SenderName} — {active.SenderRole} (День {active.DayReceived})";
            _bodyText.text = active.Body + (active.TicketCompleted ? $"\n\n<color=#2EE676>[ВЫПОЛНЕНО] {active.TicketResultNote}</color>" : "");

            if (active.HasTicketAction && !active.TicketCompleted)
            {
                string idCopy = active.Id;
                var ticketBtn = UITheme.CreateButton(
                    _actionsContainer,
                    "CompleteTicketBtn",
                    active.TicketButtonLabel,
                    UITheme.AccentGreen,
                    UITheme.TextDark,
                    18,
                    () =>
                    {
                        gm.CompleteMailTicket(idCopy);
                        Refresh();
                    });
                UITheme.AddLayout(ticketBtn, prefWidth: 460);
            }

            if (!string.IsNullOrEmpty(active.LinkedUrl))
            {
                string urlCopy = active.LinkedUrl;
                var linkBtn = UITheme.CreateButton(
                    _actionsContainer,
                    "OpenLinkBtn",
                    $"Открыть в Браузере → {urlCopy}",
                    UITheme.AccentCyan,
                    UITheme.TextDark,
                    17,
                    () => _desktop.OpenBrowserAtUrl(urlCopy));
                UITheme.AddLayout(linkBtn, prefWidth: 470);
            }
        }
    }
}
