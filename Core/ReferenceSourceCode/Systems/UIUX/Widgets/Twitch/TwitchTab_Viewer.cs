using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TwitchTab_Viewer : TwitchTabBase {
        public TwitchTab_Viewer(GameState s, TwitchSys sys, TwitchContext ctx, TwitchWidget widget)
            : base(s, sys, ctx, widget) { }

        public override void Load(Lifetime life) {
            var container = Widget.RightGroup.Container;
            int idx = 0;

            if (Ctx.SelectedViewer == null) {
                var placeholder = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, container, idx++);
                placeholder.Load("Select a viewer");
                return;
            }

            var v = Ctx.SelectedViewer;

            v.BViewerChanged.Bind(life, () => Ctx.RebuildRight?.Invoke(), invokeImmediately: false);

            Sys.ApplyRoleChanges(v);
            AddViewerRow(life, container, ref idx, v);

            var lastSeenW = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, container, idx++);
            lastSeenW.Load(IconId.CTime, "Last Seen",
                v.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

            var repLabel = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, container, idx++);
            repLabel.Load("Reputation");

            var totalRepW = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, container, idx++);
            totalRepW.Load(IconId.CUpgrade, "Total Gained", Units.Num(v.TotalReputation));

            var curRepW = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, container, idx++);
            curRepW.Load(IconId.CUpgrade, "Current", v.ReputationText);

            v.BReputation.Bind(life, _ => {
                totalRepW.SetText2(Units.Num(v.TotalReputation));
                curRepW.SetText2(v.ReputationText);
            }, notifyImmediately: false);

            void AddRepBtn(string label, int amount) {
                var btn = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                btn.Load(amount > 0 ? IconId.WPlus : IconId.WMinus, label, null, () => {
                    Ctx.SelectedViewer.AddReputation(amount);
                    Ctx.RebuildRight?.Invoke();
                });
            }
            AddRepBtn(Texts.Green("+100"), 100);
            AddRepBtn(Texts.Green("+1000"), 1000);
            AddRepBtn(Texts.Red("-100"), -100);
            AddRepBtn(Texts.Red("-1000"), -1000);

            var setZeroBtn = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            setZeroBtn.Load(IconId.WMinus, Texts.Red("Set to 0"), null, () => {
                Ctx.SelectedViewer.Reputation = 0;
                Ctx.RebuildRight?.Invoke();
            });

            var flagsLabel = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, container, idx++);
            flagsLabel.Load("Flags");

            foreach (var flag in TwitchSys.AllFlags) {
                bool hasFlag = (v.Flags & flag) == flag;
                var flagW = UISpawn.Widget<WIconText>(life,
                    UIPrefabId.WContent2Text, container, idx++);
                flagW.Load(hasFlag ? IconId.CCheck : IconId.CCross, flag.ToString());
            }

            var rolesLabel = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, container, idx++);
            rolesLabel.Load("Roles");

            foreach (var role in TwitchRole.All) {
                bool hasRole = v.HasRole(role.Key);
                var roleW = UISpawn.Widget<WIconText>(life,
                    UIPrefabId.WContent2Text, container, idx++);
                roleW.Load(hasRole ? IconId.CCheck : IconId.CCross, role.Value.Id);
            }

            var recentCmdsLabel = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, container, idx++);
            recentCmdsLabel.Load("Recent Commands");

            if (Arrays.IsEmpty(v.LastCommands)) {
                var emptyW = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, container, idx++);
                emptyW.Load(T.Empty);
            } else {
                for (int i = v.LastCommands.Count - 1; i >= 0; i--) {
                    var cmd = v.LastCommands[i];
                    var cmdW = UISpawn.Widget<WIconText2>(life,
                        UIPrefabId.WContent2Text2, container, idx++);
                    cmdW.Load(GraphicsId.IconsColorTwitchGlitch,
                        Sys.CommandName(cmd, true),
                        cmd.Time.ToLocalTime().ToShortTimeString());
                }
            }

            if (!Arrays.IsEmpty(v.CommandCounts)) {
                var cmdCountsLabel = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, container, idx++);
                cmdCountsLabel.Load("Command Counts");
                foreach (var c in TwitchSys.CommandMap.OrderBy(kv => kv.Value)) {
                    if (v.CommandCounts.TryGetValue(c.Key, out var cnt)) {
                        var countW = UISpawn.Widget<WIconText2>(life,
                            UIPrefabId.WContent2Text2, container, idx++);
                        countW.Load(GraphicsId.IconsColorTwitchGlitch,
                            Sys.Settings.AddPrefix(c.Value, true), Units.XNum(cnt));
                    }
                }
            }

            var mgmtLabel = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, container, idx++);
            mgmtLabel.Load(T.UiGroupManagement);

            var qIdx = Sys.Queue.NumberOf(v);
            if (qIdx != -1) {
                var qBtn = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                qBtn.Load(IconId.CCount, $"In Queue #{qIdx}", T.Remove, () => {
                    Sys.Queue.TryRemove(v);
                    Ctx.RebuildLeft?.Invoke();
                    Ctx.RebuildRight?.Invoke();
                });
            }

            var beingData = Sys.BeingOf(v);
            var assignTitle = beingData == null ? "Assign Being" : "Reassign Being";
            var assignBtn = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            assignBtn.Load(IconId.CPersona, assignTitle, null, () => {
                Ctx.ShowAssignBeingPopup?.Invoke();
            });

            if (beingData != null) {
                var capturedBeing = beingData;
                var unassignBtn = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, container, idx++);
                unassignBtn.Load(IconId.CCross, "Unassign Being", null, () => {
                    if (capturedBeing.Mind.Name.FullName.Contains(v.Name)) {
                        capturedBeing.Mind.Name.ParseName($"Ex {v.Name}");
                        capturedBeing.UI.AfterRename();
                    }
                    Sys.GameData.BeingIds.Remove(v.Id);
                    Ctx.RebuildLeft?.Invoke();
                    Ctx.RebuildRight?.Invoke();
                });
            }

            var deleteBtn = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            deleteBtn.Load(IconId.CWarning, "Delete Viewer", null, () => {
                TwitchSys.GlobalData.Viewers.Remove(v.Id);
                Sys.Queue.TryRemove(v);
                Ctx.SelectedViewer = null;
                Ctx.RebuildLeft?.Invoke();
                Ctx.RebuildRight?.Invoke();
            });
            UIUtils.AddTip(life, deleteBtn,
                "This will delete the viewer reputation, roles and data from the current game");
        }

        private void AddViewerRow(Lifetime life, RectTransform container, ref int idx, TwitchViewer viewer) {
            var being = Sys.BeingOf(viewer);
            bool hasBeing = being != null;

            var w = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, container, idx++);
            w.Load(hasBeing ? null : viewer.Icon, viewer.Name, null);

            if (hasBeing) {
                being.Avatar.Load(w.Image);
                UIUtils.AddHover(life, w, on => {
                    if (being.Being != null) {
                        if (on) EntityUtils.ShowEntityLink(null, being.Being, w.Rect);
                        else EntityUtils.HideEntityLink();
                    }
                });
            }

            UIUtils.AddTipFunc(life, w, () => viewer.FullTooltip(Sys));
        }
    }
}
