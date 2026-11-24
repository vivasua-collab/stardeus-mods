using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TwitchHandler : BaseWidgetHandler<TwitchWidget>, IContextPopup {
        private readonly TwitchSys sys;
        private readonly TwitchContext ctx;

        private readonly TwitchTab_Viewer viewerTab;
        private readonly TwitchTab_Queue queueTab;
        private readonly TwitchTab_Connection connectionTab;
        private readonly TwitchTab_Roles rolesTab;
        private readonly TwitchTab_Commands commandsTab;
        private readonly TwitchTab_Management managementTab;

        private Lifetime mainLifetime;
        private Lifetime leftLifetime;
        private Lifetime rightLifetime;
        private Lifetime footerLifetime;

        private enum CtxMode { RoleCfg, AssignBeing }
        private CtxMode ctxMode;
        private TwitchRole selectedRole;

        private readonly List<string> viewerFilterOpts = new List<string> {
            "All", "Subscriber", "Moderator", "VIP", "Viewer", "In Queue"
        };

        public TwitchHandler(GameState s, TwitchWidget widget) : base(s, widget) {
            sys = s.Sys.Twitch;
            sys.UI = this;

            ctx = new TwitchContext {
                RebuildLeft = RebuildLeft,
                RebuildRight = RebuildRight,
                ShowRoleConfigPopup = role => {
                    ctxMode = CtxMode.RoleCfg;
                    selectedRole = role;
                    S.UIQuery.ShowContextPopup.Ask(this, true);
                },
                ShowAssignBeingPopup = () => {
                    ctxMode = CtxMode.AssignBeing;
                    S.UIQuery.ShowContextPopup.Ask(this, true);
                },
            };

            viewerTab     = new TwitchTab_Viewer(s, sys, ctx, widget);
            queueTab      = new TwitchTab_Queue(s, sys, ctx, widget);
            connectionTab = new TwitchTab_Connection(s, sys, ctx, widget);
            rolesTab      = new TwitchTab_Roles(s, sys, ctx, widget);
            commandsTab   = new TwitchTab_Commands(s, sys, ctx, widget);
            managementTab = new TwitchTab_Management(s, sys, ctx, widget);

            widget.Initialize(s, ctx, q => { ctx.SearchStr = q; RebuildLeft(); });
            s.UIQuery.ToggleTwitch.SetResponder(ToggleTwitch);
            widget.OnShow = OnShow;
        }

        private bool ToggleTwitch(bool show) {
            widget.Toggle(show);
            return true;
        }

        private void OnShow(Lifetime lifetime) {
            mainLifetime = lifetime;
            widget.PanelHeader.SetText(BuildConnectionText());
            ctx.SelectedTab ??= TwitchTabId.Viewer;
            widget.SetSelectedTab(ctx.SelectedTab);
            RebuildLeft();
            RebuildRight();
            LoadFooter();
        }

        // ------------------------------------------------------------------ public API

        public TwitchViewer SelectedViewer => ctx.SelectedViewer;

        public void RebuildViewers() {
            Ready.WhenMainThread(this, "RebuildViewers", () => {
                if (widget.gameObject.activeSelf && mainLifetime != null) {
                    RebuildLeft();
                }
            });
        }

        public void RefreshViewer(TwitchViewer viewer) {
            if (ctx.SelectedViewer == viewer && widget.gameObject.activeSelf && mainLifetime != null) {
                RebuildRight();
            }
        }

        public void UpdateConnectionState(bool _) {
            if (!widget.gameObject.activeSelf) { return; }
            widget.PanelHeader.SetText(BuildConnectionText());
        }

        // ------------------------------------------------------------------ rebuild helpers

        private void RebuildLeft() {
            if (mainLifetime == null) { return; }
            leftLifetime?.Dispose();
            leftLifetime = mainLifetime.CreateChild("TwitchLeft");
            LoadLeft(leftLifetime);
        }

        private void RebuildRight() {
            if (mainLifetime == null) { return; }
            rightLifetime?.Dispose();
            rightLifetime = mainLifetime.CreateChild("TwitchRight");
            widget.SetSelectedTab(ctx.SelectedTab ?? TwitchTabId.Viewer);
            LoadRight(rightLifetime);
        }

        private void LoadFooter() {
            if (mainLifetime == null) { return; }
            footerLifetime?.Dispose();
            footerLifetime = mainLifetime.CreateChild("TwitchFooter");

            var container = widget.FooterGroup.Container;
            int idx = 0;

            var refreshBtn = UISpawn.Widget<WButtonIconText2>(footerLifetime,
                UIPrefabId.WContent2Button, container, idx++);
            refreshBtn.Load(IconId.WRetry, T.Refresh, null, () => {
                RebuildLeft();
                RebuildRight();
                LoadFooter();
            });
        }

        private string BuildConnectionText() {
            var globData = TwitchSys.GlobalData;
            var botName = globData.Config.BotName;
            if (string.IsNullOrWhiteSpace(botName)) { botName = T.NotConfigured; }
            if (sys.Client == null) { return Texts.Red(T.NotConfigured); }
            if (sys.Client.HasAuthFailed) { return $"{botName}: {Texts.Red("auth error")}"; }
            if (sys.Client.IsConnected) {
                return sys.Client.IsConnectConfirmed
                    ? $"{botName}: {Texts.Green("online")}"
                    : $"{botName}: {Texts.Yellow("connecting")}";
            }
            return $"{botName}: {Texts.Red("disconnected")}";
        }

        // ------------------------------------------------------------------ left panel (viewer list)

        private void LoadLeft(Lifetime life) {
            var container = widget.LeftGroup.Container;
            int idx = 0;

            var viewersLabel = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, container, idx++);
            viewersLabel.Load("Viewers");

            var filterW = UISpawn.Widget<WDropdownWithIcon>(life,
                UIPrefabId.WContent2Dropdown, container, idx++);
            filterW.Icon.SetImage(IconId.WFunnel);
            filterW.Dropdown.Load(viewerFilterOpts, ctx.FilterValue, v => {
                if (v != ctx.FilterValue) {
                    ctx.FilterValue = v;
                    RebuildLeft();
                }
            });
            UIUtils.AddTip(life, filterW, "Viewer Filter");

            string searchQuery = Strings.ToSearchString(ctx.SearchStr, true);
            string lastCat = null;
            int added = 0;

            foreach (var viewer in TwitchSys.GlobalData.Viewers.Values
                    .Where(v => FilterViewer(v))
                    .OrderByDescending(v => v.IsViewing)
                    .ThenBy(v => v.Name)) {
                if (searchQuery != null && !viewer.SearchName.Contains(searchQuery)) {
                    continue;
                }
                var cat = viewer.IsViewing ? "Viewing Now" : T.Inactive;
                if (lastCat != cat) {
                    var catLabel = UISpawn.Widget<WText>(life,
                        UIPrefabId.WContent2Header, container, idx++);
                    catLabel.Load(cat);
                    lastCat = cat;
                }
                AddViewerRow(life, container, ref idx, viewer);
                added++;
            }

            if (added == 0) {
                var emptyW = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, container, idx++);
                emptyW.Load(T.Empty);
            }
        }

        private bool FilterViewer(TwitchViewer viewer) {
            return ctx.FilterValue switch {
                0 => true,
                1 => viewer.isSubscriber,
                2 => viewer.IsMod,
                3 => viewer.IsVIP,
                4 => viewer.Flags == TwitchViewerFlags.Viewer,
                5 => (sys.Queue?.NumberOf(viewer) ?? -1) > -1,
                _ => false,
            };
        }

        private void AddViewerRow(Lifetime life, RectTransform container,
                ref int idx, TwitchViewer viewer) {
            var being = sys.BeingOf(viewer);
            bool hasBeing = being != null;
            bool isSelected = ctx.SelectedViewer == viewer;

            string nameText = isSelected ? Texts.Accented(viewer.Name, false) : viewer.Name;
            string text2 = isSelected ? Texts.Accented(GlyphMap.ArrowRightLarge) : T.Details;

            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, container, idx++);
            var capturedViewer = viewer;
            w.Load(hasBeing ? null : viewer.Icon, nameText, text2, () => {
                ctx.SelectViewer(capturedViewer);
            });

            if (hasBeing) {
                being.Avatar.Load(w.Image);
                UIUtils.AddHover(life, w, on => {
                    if (being.Being != null) {
                        if (on) EntityUtils.ShowEntityLink(null, being.Being, w.Rect);
                        else EntityUtils.HideEntityLink();
                    }
                });
            }

            UIUtils.AddTipFunc(life, w, () => viewer.FullTooltip(sys));
        }

        // ------------------------------------------------------------------ right panel

        private void LoadRight(Lifetime life) {
            switch (ctx.SelectedTab) {
                case TwitchTabId.Viewer:     viewerTab.Load(life);     break;
                case TwitchTabId.Queue:      queueTab.Load(life);      break;
                case TwitchTabId.Connection: connectionTab.Load(life); break;
                case TwitchTabId.Roles:      rolesTab.Load(life);      break;
                case TwitchTabId.Commands:   commandsTab.Load(life);   break;
                case TwitchTabId.Management: managementTab.Load(life); break;
            }
        }

        // ------------------------------------------------------------------ IContextPopup

        private static readonly int popupHash = Hashes.S("TwitchContextPopup");
        public int UniquePopupHash => popupHash;
        public bool ShowInScreenCenter => true;
        public void OnPopupOpened() { }
        public void OnPopupClosed() { }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("TwitchCtx");
            wFooter.SetActiveFast(false);
            return ctxMode == CtxMode.RoleCfg
                ? LoadRoleConfigContent(life, wContent)
                : LoadAssignBeingContent(life, wContent);
        }

        private string LoadRoleConfigContent(Lifetime life, WGroup wContent) {
            if (selectedRole == null) { return "Role Config"; }
            var r = selectedRole;
            var block = UISpawn.Block(life, wContent, 0);
            int idx = 0;

            var instantHeader = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, block.Container, idx++);
            instantHeader.Load("Instantly Available For");

            foreach (var flag in TwitchSys.AllFlags) {
                var capturedFlag = flag;
                var w = UISpawn.Widget<WToggleText>(life,
                    UIPrefabId.WContent2Toggle, block.Container, idx++);
                w.Load(flag.ToString(), (GetRoleData(r).AddForFlags & flag) == flag, v => {
                    var rd = GetRoleData(r);
                    if (v) { rd.AddForFlags |= capturedFlag; } else { rd.AddForFlags &= ~capturedFlag; }
                    UpdateRoleData(rd);
                });
            }

            var othersHeader = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, block.Container, idx++);
            othersHeader.Load("For Others");

            var repInput = UISpawn.Widget<WInputAmtIconText2>(life,
                UIPrefabId.WContent2Text2Amt, block.Container, idx++);
            repInput.Item.Load(IconId.CStockMarket, "Required Reputation", "0 = disabled");
            repInput.Amt.Load(GetRoleData(r).AddAtReputation, v => {
                int newV = Mathf.RoundToInt(v);
                var rd = GetRoleData(r);
                if (rd.AddAtReputation != newV) {
                    rd.AddAtReputation = newV;
                    UpdateRoleData(rd);
                }
            }, v => Maths.Clamp(v, 0, int.MaxValue));
            UIUtils.AddTip(life, repInput,
                "Role will be added to viewers who have this reputation amount");

            return $"Role: {r.Id}";
        }

        private string LoadAssignBeingContent(Lifetime life, WGroup wContent) {
            if (ctx.SelectedViewer == null) { return "Assign Being"; }
            var v = ctx.SelectedViewer;
            var curBeing = sys.BeingOf(v);
            var block = UISpawn.Block(life, wContent, 0);
            int idx = 0;

            var beings = S.Query.AllBeingData.Ask();
            string lastCat = null;
            foreach (var b in beings.Values
                    .Where(b => b.IsPlayer && b.HasStateFlag(BeingState.Alive))
                    .OrderBy(b => b.Species.Name)
                    .ThenBy(b => b.Name)) {
                var cat = b.Species.Name;
                if (cat != lastCat) {
                    lastCat = cat;
                    var catLabel = UISpawn.Widget<WText>(life,
                        UIPrefabId.WContent2Header, block.Container, idx++);
                    catLabel.Load(cat);
                }

                TwitchViewer assignedTo = null;
                foreach (var ass in sys.GameData.BeingIds) {
                    if (ass.Value == b.Id) {
                        TwitchSys.GlobalData.Viewers.TryGetValue(ass.Key, out assignedTo);
                        break;
                    }
                }
                var avatar = S.Query.GetBeingAvatar.Ask(b);
                var capturedB = b;
                var name = b.Name;

                if (assignedTo != null) {
                    var nameText = b == curBeing ? Texts.Accented(name, false) : name;
                    var row = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    row.Load(null, nameText, Texts.Grey("Assigned"), null);
                    avatar?.Load(row.Image);
                    UIUtils.AddHover(life, row, on => {
                        if (b.Being != null) {
                            if (on) EntityUtils.ShowEntityLink(null, b.Being, row.Rect);
                            else EntityUtils.HideEntityLink();
                        }
                    });
                } else {
                    var assignText = curBeing == null ? T.VerbAssign : T.VerbReassign;
                    var btn = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent2Button, block.Container, idx++);
                    btn.Load(null, name, assignText, () => {
                        foreach (var ba in sys.GameData.BeingIds) {
                            if (ba.Value == capturedB.Id) {
                                sys.GameData.BeingIds.Remove(ba.Key);
                                break;
                            }
                        }
                        if (curBeing != null && v.Name.Contains(curBeing.Name)) {
                            curBeing.Mind.Name.ParseName($"Ex {v.Name}");
                            curBeing.UI.AfterRename();
                        }
                        sys.AssignBeing(v, capturedB, true);
                        S.UIQuery.ShowContextPopup.Ask(this, false);
                    });
                    avatar?.Load(btn.Image);
                    UIUtils.AddHover(life, btn, on => {
                        if (b.Being != null) {
                            if (on) EntityUtils.ShowEntityLink(null, b.Being, btn.Rect);
                            else EntityUtils.HideEntityLink();
                        }
                    });
                }
            }

            return curBeing == null ? "Assign Being" : "Reassign Being";
        }

        private TwitchRoleData GetRoleData(TwitchRole role) {
            if (!sys.Settings.Roles.TryGetValue(role.IdH, out var rd)) {
                rd = TwitchRoleData.DefaultFor(role);
            }
            return rd;
        }

        private void UpdateRoleData(in TwitchRoleData rd) {
            sys.Settings.Roles[rd.RoleId] = rd;
            sys.ApplyAllRoleChanges();
        }
    }
}
