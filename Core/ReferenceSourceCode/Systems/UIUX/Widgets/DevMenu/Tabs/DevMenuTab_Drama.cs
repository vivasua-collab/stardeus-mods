#if !STARDEUS_DEMO
using System;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Drama;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class DevMenuTab_Drama : DevMenuTab {
        public const string TabIdConst = "Drama";
        public override string TabId => TabIdConst;
        public override string TabName => "Drama";

        private enum Mode { None, Interactions, Opportunities, Chats, BeingClusters, Proximity }

        private Mode selectedMode;
        private bool pauseOnChange;
        private bool skipEmpty;
        private bool skipNotSociable;

        private int lastChatCount;
        private int lastClusterCount;
        private int lastProxCount;
        private int lastOpCount;
        private int lastIntCount;

        private Lifetime tabLifetime;
        private Lifetime detailsLifetime;
        private bool hasListener;

        public DevMenuTab_Drama(GameState s) : base(s) { }

        public override void LoadContent(Lifetime life, WGroup group, ref int gIdx,
                SubPanelWidget subPanel, Action reload) {
            tabLifetime = life;

            // Tick listener for pauseOnChange logic
            life.OnDispose(OnClose);
            S.Clock.OnTick.AddListener(CheckPauseOnChange);
            hasListener = true;
            var block = UISpawn.Block(life, group, gIdx++);
            int idx = 0;

            UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++).Load("Drama");

            WBase freshSelectedBtn = null;

            AddModeButton(life, block.Container, ref idx, Mode.Interactions, "Interactions", ref freshSelectedBtn, subPanel, reload);
            AddModeButton(life, block.Container, ref idx, Mode.Opportunities, "Opportunities", ref freshSelectedBtn, subPanel, reload);
            AddModeButton(life, block.Container, ref idx, Mode.Chats, "Chats", ref freshSelectedBtn, subPanel, reload);
            AddModeButton(life, block.Container, ref idx, Mode.BeingClusters, "Being Clusters", ref freshSelectedBtn, subPanel, reload);
            AddModeButton(life, block.Container, ref idx, Mode.Proximity, "Proximity Pairs", ref freshSelectedBtn, subPanel, reload);

            if (selectedMode != Mode.None && freshSelectedBtn != null) {
                ShowModeDetails(freshSelectedBtn, subPanel, reload);
            }
        }

        private void AddModeButton(Lifetime life, UnityEngine.RectTransform container, ref int idx,
                Mode mode, string label, ref WBase freshSelectedBtn,
                SubPanelWidget subPanel, Action reload) {
            string icon = selectedMode == mode ? IconId.CCheck : IconId.CInfo;
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(icon, label, "Show", () => {
                if (subPanel.IsShowing && selectedMode == mode) {
                    selectedMode = Mode.None;
                    subPanel.Close();
                    reload();
                    return;
                }
                selectedMode = mode;
                ShowModeDetails(btn, subPanel, reload);
                reload();
            });
            if (selectedMode == mode) { freshSelectedBtn = btn; }
        }

        private void ShowModeDetails(WBase anchor, SubPanelWidget subPanel, Action reload) {
            detailsLifetime?.Dispose();
            detailsLifetime = tabLifetime.CreateChild("DramaDetails");
            var l = detailsLifetime;

            subPanel.SetTitle(selectedMode.ToString());
            int bIdx = 0;

            var block = UISpawn.Block(l, subPanel.Container, bIdx++);
            int idx = 0;

            // Refresh button
            var refreshBtn = UISpawn.Widget<WButtonText>(l, UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
            refreshBtn.Load("Refresh", () => {
                ShowModeDetails(anchor, subPanel, reload);
                reload();
            });

            switch (selectedMode) {
                case Mode.Interactions: AddInteractions(l, block.Container, ref idx); break;
                case Mode.Opportunities: AddOpportunities(l, block.Container, ref idx); break;
                case Mode.Chats: AddChats(l, block.Container, ref idx); break;
                case Mode.BeingClusters: AddClusters(l, block.Container, ref idx); break;
                case Mode.Proximity: AddProximity(l, block.Container, ref idx); break;
            }

            subPanel.ShowFooter(false);
            subPanel.Show(anchor, true);
        }

        private void AddInteractions(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Social Interactions");
            AddPauseOnChangeToggle(l, container, ref idx);

            var inter = S.Query.GetSocialInteractions.Ask();
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++).Load(IconId.CCount, "Count", Units.XNum(inter.Count));

            if (inter.Count == 0) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(IconId.CInfo, T.Empty);
                return;
            }
            int cnt = 0;
            int left = 0;
            for (int i = inter.Count - 1; i >= 0; i--) {
                var soc = inter[i];
                if (soc.Icon == null || soc.Title == null) { continue; }
                string time = soc.CreatedAt > 0 ? Units.TicksAgo((int)(G.Ticks - soc.CreatedAt)) : string.Empty;
                var row = UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++);
                row.Load(soc.Icon, soc.Title, time);
                if (soc.Desc != null) { UIUtils.AddTip(l, row, soc.Desc); }
                if (cnt++ > 300) {
                    left = inter.Count - cnt;
                    break;
                }
            }
            if (left > 0) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(
                    IconId.CInfo, $"And {Units.XNum(left)} more...");
            }
        }

        private void AddOpportunities(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Social Opportunities");
            AddPauseOnChangeToggle(l, container, ref idx);

            var ops = S.Query.GetSocialOpportunities.Ask();
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++).Load(IconId.CCount, "Count", Units.XNum(ops.Count));

            if (ops.Count == 0) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(IconId.CInfo, T.Empty);
                return;
            }
            foreach (var op in ops) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(IconId.CInfo, op.ToString());
            }
        }

        private void AddChats(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Ongoing Chats");
            AddPauseOnChangeToggle(l, container, ref idx);

            var chats = S.Query.GetBeingChats.Ask();
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++).Load(IconId.CCount, "Count", Units.XNum(chats.Count));

            if (chats.Count == 0) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(IconId.CInfo, T.Empty);
                return;
            }

            string lastCtx = "none";
            foreach (var chat in chats.Values.OrderBy(ch => ch.Context)) {
                if (lastCtx != chat.Context) {
                    lastCtx = chat.Context;
                    UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load(lastCtx ?? "No Context");
                }
                string title = $"{chat.B1.Mind.LinkToShortName} & {chat.B2.Mind.LinkToShortName}";
                string timeStr = Units.TicksToTime(G.Ticks - chat.StartTick);
                var row = UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++);
                row.Load(chat.HasStarted ? IconId.CCheck : IconId.CTime, title, timeStr);
            }
        }

        private void AddClusters(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Being Clusters");
            AddPauseOnChangeToggle(l, container, ref idx);
            AddSkipEmptyToggle(l, container, ref idx);
            AddSkipNotSociableToggle(l, container, ref idx);

            var clusters = S.Query.GetBeingClusters.Ask();
            var expClusters = S.Query.GetExpeditionClusters.Ask();

            if (clusters.Count + expClusters.Count == 0) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(IconId.CInfo, T.Empty);
                return;
            }

            if (expClusters.Count > 0) {
                UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Expedition");
                foreach (var cl in expClusters) {
                    AddClusterRow(l, container, ref idx, cl.Value);
                }
                UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Normal");
            }
            foreach (var cl in clusters.Values.OrderBy(ch => ch.RegId)) {
                AddClusterRow(l, container, ref idx, cl);
            }
        }

        private void AddClusterRow(Lifetime l, UnityEngine.RectTransform container, ref int idx, BeingCluster cl) {
            if (skipEmpty && cl.All.Count == 0) { return; }
            if (skipNotSociable && !cl.IsSociable) { return; }
            var row = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, container, idx++);
            row.Load(IconId.CPreviewHuman01, cl.ToString(), Units.XNum(cl.All.Count), () => {
                if (cl.All.Count > 0) { EntityUtils.CameraFocusOn(cl.All[0].Being); }
                else { UISounds.PlayActionDenied(); }
            });
            UIUtils.AddTipFunc(l, row, () => {
                var sb = Caches.StringBuilder;
                sb.Clear();
                foreach (var b in cl.All) {
                    string soc = cl.FriendlySociable.Contains(b) ? "Sociable" : string.Empty;
                    string socIdle = cl.FriendlySociableIdle.Contains(b) ? "Idle" : string.Empty;
                    string dead = cl.Dead.Contains(b) ? "Dead" : string.Empty;
                    sb.AppendFormat(" - {0} | {1} {2} {3}\n", b.Mind.LinkToShortName, soc, socIdle, dead);
                }
                return sb.ToString();
            });
            UIUtils.AddHover(l, row, on => {
                if (on && cl.All.Count > 0) { EntityUtils.ShowEntityLink(null, cl.All[0].Being, true); }
                else { EntityUtils.HideEntityLink(); }
            });
        }

        private void AddProximity(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            UISpawn.Widget<WText>(l, UIPrefabId.WContent2Header, container, idx++).Load("Being Proximity Pairs");
            AddPauseOnChangeToggle(l, container, ref idx);

            var inProx = S.Query.GetBeingProximity.Ask();
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++).Load(IconId.CCount, "Count", Units.XNum(inProx.Count));

            if (inProx.Count == 0) {
                UISpawn.Widget<WIconText>(l, UIPrefabId.WContent2Text, container, idx++).Load(IconId.CInfo, T.Empty);
                return;
            }
            foreach (var inPr in inProx) {
                var b1 = inPr.Value.Item1;
                var b2 = inPr.Value.Item2;
                var row = UISpawn.Widget<WIconText2>(l, UIPrefabId.WContent2Text2, container, idx++);
                row.Load(b1.Being?.Definition.Preview ?? IconId.CPreviewHuman01,
                    $"{b1.Mind.LinkToShortName} & {b2.Mind.LinkToShortName}", string.Empty);
            }
        }

        private void AddPauseOnChangeToggle(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(pauseOnChange ? IconId.CCheck : IconId.CCross, "Pause On Change", T.Toggle, () => {
                pauseOnChange = !pauseOnChange;
            });
        }

        private void AddSkipEmptyToggle(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(skipEmpty ? IconId.CCheck : IconId.CCross, "Skip Empty", T.Toggle, () => {
                skipEmpty = !skipEmpty;
            });
        }

        private void AddSkipNotSociableToggle(Lifetime l, UnityEngine.RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(l, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(skipNotSociable ? IconId.CCheck : IconId.CCross, "Skip Not Sociable", T.Toggle, () => {
                skipNotSociable = !skipNotSociable;
            });
        }

        private void CheckPauseOnChange(long ticks) {
            if (!pauseOnChange || G.ClockSpeed == ClockSpeed.Stopped) { return; }
            CheckCountPause(ref lastIntCount, S.Query.GetSocialInteractions.Ask().Count, "interactions");
            CheckCountPause(ref lastOpCount, S.Query.GetSocialOpportunities.Ask().Count, "opportunities");
            CheckCountPause(ref lastChatCount, S.Query.GetBeingChats.Ask().Count, "chats");
            CheckCountPause(ref lastProxCount, S.Query.GetBeingProximity.Ask().Count, "proximity");
            var clusters = S.Query.GetBeingClusters.Ask();
            var expClusters = S.Query.GetExpeditionClusters.Ask();
            CheckCountPause(ref lastClusterCount, clusters.Count + expClusters.Count, "clusters");
        }

        private void CheckCountPause(ref int last, int current, string label) {
            if (last != current) {
                last = current;
                D.Warn("Drama: pausing on {0} count change to {1}", label, current);
                S.Clock.Pause();
            }
        }

        public override void OnClose() {
            if (hasListener) {
                hasListener = false;
                S.Clock.OnTick.RemoveListener(CheckPauseOnChange);
            }
        }
    }
}
#endif
