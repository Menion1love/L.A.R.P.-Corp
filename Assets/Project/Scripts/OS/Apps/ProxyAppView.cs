using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Project.LarpCore;

namespace Project.LinuxOS
{
    /// <summary>
    /// Приложение «Proxy / VPN» внутри ОС:
    /// позволяет сменить интернет-прокси (маршрут IP) и снизить уровень Trace.
    /// Используется как в обучении механике Trace, так и в свободном режиме.
    /// </summary>
    public class ProxyAppView : MonoBehaviour
    {
        private class ProxyNode
        {
            public string Id;
            public string Label;
            public string FakeIp;
            public int Cost;
            public int TraceDrop;
        }

        private readonly List<ProxyNode> _nodes = new List<ProxyNode>
        {
            new ProxyNode { Id = "nl_ams", Label = "NL — Amsterdam Relay #01", FakeIp = "185.220.101.14", Cost = 0, TraceDrop = 25 },
            new ProxyNode { Id = "ch_zur", Label = "CH — Zurich Tunnel #02",   FakeIp = "194.230.148.88", Cost = 0, TraceDrop = 20 },
            new ProxyNode { Id = "is_rek", Label = "IS — Reykjavik Node #04",  FakeIp = "82.221.105.61",  Cost = 45, TraceDrop = 30 },
            new ProxyNode { Id = "jp_tok", Label = "JP — Tokyo Onion #09",     FakeIp = "103.214.169.22", Cost = 75, TraceDrop = 40 }
        };

        private readonly HashSet<string> _usedFreeNodes = new HashSet<string>();

        private TextMeshProUGUI _currentRouteText;
        private TextMeshProUGUI _traceInfoText;
        private RectTransform _listContent;

        public void Initialize(RectTransform root)
        {
            var v = root.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10;
            v.padding = new RectOffset(16, 16, 14, 14);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var hdr = UITheme.CreateText(root, "Hdr", "PROXY / VPN ROUTING", 20f, UITheme.WindowOrange, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UITheme.AddLayout(hdr, prefHeight: 30f);

            var statusBox = UITheme.CreatePanel(root, "StatusBox", UITheme.BgCard);
            UITheme.AddLayout(statusBox, prefHeight: 74f);
            var sv = statusBox.gameObject.AddComponent<VerticalLayoutGroup>();
            sv.spacing = 4;
            sv.padding = new RectOffset(14, 14, 10, 10);
            sv.childControlWidth = true;
            sv.childControlHeight = true;

            _currentRouteText = UITheme.CreateText(statusBox, "Route", "", 16f, UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, monospace: true);
            UITheme.AddLayout(_currentRouteText, prefHeight: 26f);

            _traceInfoText = UITheme.CreateText(statusBox, "TraceInfo", "", 15f, UITheme.TextMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, monospace: true);
            UITheme.AddLayout(_traceInfoText, prefHeight: 24f);

            var (scroll, content) = UITheme.CreateVerticalScrollView(root, "ProxyScroll", UITheme.TerminalBg, 8f, 10);
            UITheme.AddLayout(scroll, flexHeight: 1f);
            _listContent = content;

            Refresh();
        }

        public void Refresh()
        {
            var ps = PlayerState.Instance;
            var gm = GameManager.Instance;
            if (_listContent == null) return;

            string activeNode = ps != null ? ps.ActiveProxyNode : "Direct (Home IP)";
            int trace = gm != null ? gm.Trace : 0;

            _currentRouteText.text = $"Активный маршрут: {activeNode}";
            _traceInfoText.text = $"Текущий Trace: {trace}%  |  Смена прокси запутывает след СБ и снижает тревогу.";
            _traceInfoText.color = trace >= 50 ? UITheme.AccentRed : (trace >= 25 ? UITheme.AccentAmber : UITheme.TerminalGreen);

            UITheme.ClearChildren(_listContent);

            foreach (var node in _nodes)
            {
                bool isCurrent = activeNode.Contains(node.FakeIp);
                int effectiveCost = (node.Cost == 0 && _usedFreeNodes.Contains(node.Id)) ? 35 : node.Cost;

                var card = UITheme.CreatePanel(_listContent, "Node_" + node.Id, isCurrent ? UITheme.BgCardHover : UITheme.BgCard);
                UITheme.AddLayout(card, prefHeight: 78f);

                var h = card.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 12;
                h.padding = new RectOffset(14, 14, 10, 10);
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandHeight = true;
                h.childForceExpandWidth = false;

                var infoCol = UITheme.CreatePanel(card, "Info", new Color(0, 0, 0, 0));
                UITheme.AddLayout(infoCol, flexWidth: 1f);
                var iv = infoCol.gameObject.AddComponent<VerticalLayoutGroup>();
                iv.spacing = 4;
                iv.childControlWidth = true;
                iv.childControlHeight = true;

                var t1 = UITheme.CreateText(infoCol, "T1", $"{node.Label}  [{node.FakeIp}]", 16f, isCurrent ? UITheme.TerminalGreen : UITheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, monospace: true);
                UITheme.AddLayout(t1, prefHeight: 26f);

                string costStr = effectiveCost == 0 ? "БЕСПЛАТНО (1-я смена)" : $"Стоимость: ${effectiveCost}";
                var t2 = UITheme.CreateText(infoCol, "T2", $"Снижение Trace: -{node.TraceDrop}%  |  {costStr}", 14f, UITheme.TextMuted);
                UITheme.AddLayout(t2, prefHeight: 22f);

                ProxyNode captured = node;
                int capturedCost = effectiveCost;
                var btn = UITheme.CreateButton(
                    card,
                    "SwitchBtn",
                    isCurrent ? "АКТИВЕН" : (capturedCost == 0 ? "ПЕРЕКЛЮЧИТЬ" : $"СМЕНИТЬ (${capturedCost})"),
                    isCurrent ? UITheme.BgPanel : UITheme.WindowOrange,
                    UITheme.TextPrimary,
                    14f,
                    () => SwitchToNode(captured, capturedCost));
                btn.interactable = !isCurrent;
                UITheme.AddLayout(btn, prefWidth: 165f);
            }
        }

        private void SwitchToNode(ProxyNode node, int cost)
        {
            var gm = GameManager.Instance;
            var ps = PlayerState.Instance;
            if (gm == null || ps == null) return;

            if (cost > 0 && gm.Money < cost)
            {
                AudioFeedbackManager.Instance?.PlayErrorBuzzer();
                gm.PostToast($"Недостаточно средств для смены прокси (нужно ${cost}).");
                return;
            }

            if (cost > 0)
                gm.SpendMoney(cost);
            else
                _usedFreeNodes.Add(node.Id);

            ps.SetProxyNode($"{node.Label} ({node.FakeIp})");
            gm.ReduceTrace(node.TraceDrop, $"Смена прокси на {node.FakeIp}");
            Day1StoryController.Instance?.NotifyProxySwitched();
            Refresh();
        }
    }
}
