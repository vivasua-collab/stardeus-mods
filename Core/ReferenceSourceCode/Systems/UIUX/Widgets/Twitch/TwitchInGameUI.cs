#if false
using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Twitch {
    public sealed class TwitchInGameUI : IUIDataProvider, IUITwoColumnProvider,
            IUIContextMenuProvider {
        private enum CtxMode {
            RoleCfg,
            AssignBeing,
        }
        private CtxMode ctxMode;
        public Entity Entity => null;
        public bool UIAutoPackLeft => false;
        public bool UIAutoPackRight => true;
        private readonly GameState S;
        private readonly TwitchSys sys;
        public readonly List<OverlayInfo> Overlays;
        private bool isShowingOverlay;
        public TwitchViewer SelectedViewer;
        private UDB selectedViewerUDB;

        public TwitchInGameUI(TwitchSys sys) {
            this.sys = sys;
            S = sys.S;
            Overlays = new List<OverlayInfo>() {
                new OverlayInfo(OverlayType.Special, OverlayId.Twitch,
                    GraphicsId.IconsColorTwitchGlitch, false)
                        .WithoutMoreInfo()
            };
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
        }
        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (isShowingOverlay && !on) {
                // close
                S.Sig.HideTwoColumnPanel.Send(this);
                isShowingOverlay = false;
            } else if (!isShowingOverlay && on && info.IdH == OverlayIdH.Twitch) {
                // open
                S.Sig.ShowTwoColumnPanel.Send(this, false);
                isShowingOverlay = true;
            }
        }

        public string GetName() {
            return "TwitchUI";
        }

        private UDB leftLabel;
        private UDB rightLabel;
        private UDB ircInput;
        private UDB searchInput;
        private UDB viewerFilter;
        private UDB welcomeMsg;
        private string rawCmd;
        private string searchStr;
        private int curFilterValue;
        private UDB settingsUDB;

        private readonly List<string> viewerOpts = new() {
            "All",
            "Subscriber",
            "Moderator",
            "VIP",
            "Viewer",
            "In Queue"
        };
        private bool FilterViewer(TwitchViewer viewer) {
            switch (curFilterValue) {
                case 0: return true;
                case 1: return viewer.isSubscriber;
                case 2: return viewer.IsMod;
                case 3: return viewer.IsVIP;
                // exclusively viewer
                case 4: return viewer.Flags == TwitchViewerFlags.Viewer;
                case 5: {
                    return (sys.Queue?.NumberOf(viewer) ?? -1) > -1;
                }
                default: return false;
            }
        }

        public void UpdateConnectionState(bool wasUpdated) { }

        private void UpdateSettingsBtnText() {
            settingsUDB.UpdateText2(
                SelectedViewer == null ? Texts.Accented(GlyphMap.ArrowRightLarge) : T.VerbConfigure);
        }

        private void AddDebugInput(List<UDB> res) {
            res.Add(ircInput ??= UDB.Create(this, UDBT.DTextInputBtn, IconId.CThoughtBubble, null)
                .WithText2("Chat")
                .WithValueChangeFunction((b, v) => {
                    rawCmd = (string)v;
                })
                .WithClickFunction(() => {
                    var cmd = ircInput.Title.Trim();
                    if (!string.IsNullOrEmpty(cmd)) {
                        sys.Client.SendRaw(cmd);
                        ircInput.UpdateTitle(null);
                    }
                }));
            ircInput.UpdateTitle(rawCmd);
        }

        private BeingData AddViewerUDB(List<UDB> res, TwitchViewer viewer, bool details) {
            BeingData being = sys.BeingOf(viewer);
            UDB viewerUDB;
            if (being != null) {
                viewerUDB = UDB.Create(this, details ? UDBT.IText : UDBT.ITextBtn, null, null);
                var avatar = S.Query.GetBeingAvatar.Ask(being);
                viewerUDB.WithSprite(avatar.Sprite);
                viewerUDB.WithIconHoverFunction((s) => {
                    if (being.Being != null) {
                        EntityUtils.ShowEntityLink(null, being.Being, s);
                    } else {
                        EntityUtils.HideEntityLink();
                    }
                }).WithIconClickFunction(() => {
                    BeingUtils.Select(being, true);
                });
                viewerUDB.WithText("In Game");
            } else {
                viewerUDB = UDB.Create(this, details ? UDBT.DText : UDBT.DTextBtn,
                        viewer.Icon, null);
                var qNum = sys.Queue?.NumberOf(viewer) ?? -1;
                if (qNum == -1) {
                    viewerUDB.WithText("Not In Queue");
                } else {
                    viewerUDB.WithText("In Queue");
                }
            }
            if (SelectedViewer == viewer && !details) {
                selectedViewerUDB = viewerUDB;
                viewerUDB.UpdateTitle(Texts.Accented(viewer.Name, false));
                viewerUDB.UpdateText2(Texts.Accented(GlyphMap.ArrowRightLarge));
            } else {
                viewerUDB.UpdateTitle(viewer.Name);
                if (!details) {
                    viewerUDB.UpdateText2(T.Details);
                }
            }
            if (!details) {
                viewerUDB.WithClickFunction(() => {
                    if (SelectedViewer != viewer) {
                        if (SelectedViewer != null) {
                            selectedViewerUDB.UpdateTitle(SelectedViewer.Name);
                            selectedViewerUDB.UpdateText2(T.Details);
                        }
                        viewerUDB.UpdateTitle(Texts.Accented(viewer.Name, false));
                        viewerUDB.UpdateText2(Texts.Accented(GlyphMap.ArrowRightLarge));
                    }
                    SelectedViewer = viewer;
                    UpdateSettingsBtnText();
                    selectedViewerUDB = viewerUDB;
                    rightLabel.NeedsListRebuild = true;
                });
            }
            viewerUDB.WithTooltipFunction((b) => viewer.FullTooltip(sys));
            res.Add(viewerUDB);
            return being;
        }


        public void GetUIDetails2(List<UDB> res) {
            if (SelectedViewer == null) {
                ShowSettings(res);
            } else {
                ShowSelectedViewer(res);
            }
        }

        private readonly List<string> prefixOpts = new() {
            "!",
            "!s",
            "!sd",
            "!x",
            "!g",
        };

        private readonly List<string> viewerQueueOpts = ((TwitchSettings.ViewerQueueMode[])
            Enum.GetValues(typeof(TwitchSettings.ViewerQueueMode)))
                .Select(v => $"Queue mode: {v}").ToList();

        private readonly Dictionary<int, UDB> commandBlocks = new();
        private UDB repGainUDB;

        private void ShowSettings(List<UDB> res) {
            rightLabel = UDB.Create(this, UDBT.DLabel, null, "Connection");
            res.Add(rightLabel);

            res.Add(welcomeMsg ??= UDB.Create(this, UDBT.DTextInput, IconId.CWave, sys.Settings.WelcomeMsg)
                .WithText("Custom welcome message")
                .WithTooltip("Custom welcome message")
                .WithValueChangeFunction((b, v) => {
                    sys.Settings.WelcomeMsg = (string) v;
                }));
            AddToggle(res, "Announce Connect/Disconnect",
                () => sys.Settings.AnnounceConnect,
                (v) => sys.Settings.AnnounceConnect = v);
            res.Add(UDBCache.Label("Viewer Queue"));
            res.Add(UDB.Create(this, UDBT.DDropdown, IconId.WList, "Queue Mode")
                .WithCustomData(viewerQueueOpts)
                .WithValueChangeFunction((b, v) => {
                    var mode = (TwitchSettings.ViewerQueueMode)(int) v;
                    sys.Settings.QueueMode = mode;
                }).WithValueOf((int) sys.Settings.QueueMode));

            // Queue skip inactive
            AddToggle(res, "Skip Inactive Viewers",
                () => sys.Settings.QueueSkipInactive,
                (v) => sys.Settings.QueueSkipInactive = v);

            // Queue Sub Priority
            AddToggle(res, "Prioritize Subscribers",
                () => sys.Settings.SubQueuePriority,
                (v) => sys.Settings.SubQueuePriority = v);
            res.Add(UDBCache.Label("Roles"));
            foreach (var r in TwitchRole.All.Values.OrderBy(v => v.Id)) {
                res.Add(UDB.Create(this, UDBT.DTextBtn, IconId.WPerson, $"Role: {r.Id}")
                    .WithText2(T.VerbConfigure)
                    .WithClickFunction(() => {
                        ctxMode = CtxMode.RoleCfg;
                        selectedRole = r;
                        S.Sig.ShowContextMenu.Send(this);
                    }));
            }
            res.Add(UDBCache.Label("Management"));
            res.Add(UDB.Create(this, UDBT.DBtnLong, IconId.CWarning, "Delete All Viewer Data")
                .WithLongPressFunction(() => {
                    sys.DeleteAllViewers();
                    RebuildViewers();
                }));
            res.Add(UDB.Create(this, UDBT.DBtnLong, IconId.CWarning, "Reset All Reputation")
                .WithLongPressFunction(() => {
                    sys.ResetAllPoints();
                    RebuildViewers();
                }));
            res.Add(UDBCache.Label("Commands"));

            int cmdPrefixIdx = 0;
            var custPref = sys.Settings.CommandPrefix;
            if (custPref != null) {
                cmdPrefixIdx = prefixOpts.IndexOf(custPref);
                if (cmdPrefixIdx == -1) {
                    cmdPrefixIdx = 0;
                    sys.Settings.CommandPrefix = null;
                }
            }
            res.Add(UDB.Create(this, UDBT.DDropdown, IconId.WList, "Command Prefix")
                .WithCustomData(prefixOpts)
                .WithTooltip("Command Prefix")
                .WithValueChangeFunction((b, v) => {
                    var opt = (int) v;
                    sys.Settings.CommandPrefix = prefixOpts[opt];
                    rightLabel.NeedsListRebuild = true;
                }).WithValueOf(cmdPrefixIdx));


            repGainUDB ??= UDB.Create(this, UDBT.IAmtInput, IconId.CUpgrade, "Reputation Gain")
                .WithIconClickFunction(() => {
                })
                .WithTooltip("Viewers will gain this amount of Reputation per hour of active watch time")
                .WithText2("Per Hour:")
                .WithRange(0, 999999)
                .WithValueChangeFunction((b, v) => {
                    var intV = (int) (float) v;
                    sys.Settings.RepGainPerHour = intV;
                    repGainUDB.UpdateValue(intV);
                })
                .WithClickFunction(() => {
                    UpdateCommandCostAmt(repGainUDB, 1);
                })
                .WithClick2Function(() => {
                    UpdateCommandCostAmt(repGainUDB, -1);
                });
            repGainUDB.UpdateValue(sys.Settings.RepGainPerHour);
            res.Add(repGainUDB);

            foreach (var cmd in TwitchSys.CommandMap.OrderBy(kv => kv.Value)) {
                var enabled = sys.Settings.IsCommandEnabled(cmd.Key);
                var cost = sys.CommandCost(cmd.Key);
                var cmdName = sys.Settings.AddPrefix(cmd.Value, true);
                if (!commandBlocks.TryGetValue(cmd.Key, out var b)) {
                    b = UDB.Create(this, UDBT.IAmtInput, null, null)
                        .WithRange(0, 999999)
                        .WithIconClickFunction(() => {
                            var isOn = sys.Settings.IsCommandEnabled(cmd.Key);
                            isOn = !isOn;
                            sys.Settings.SetCommandEnabled(cmd.Key, isOn);
                            b.UpdateIcon(isOn ? IconId.CCheck : IconId.CCross);
                            b.UpdateTitle(isOn ? cmdName : Texts.Strike(cmdName));
                        })
                        .WithValueChangeFunction((b, v) => {
                            var costs = TwitchSys.GlobalData.CommandCosts;
                            var intV = (int) (float) v;
                            costs[cmd.Key] = intV;
                            TwitchSys.CommandCosts[cmd.Key] = intV;
                            b.UpdateValue(intV);
                        })
                        .WithTooltip(TwitchSys.HelpLines[cmd.Key])
                        .WithText2("Rep Cost:")
                        .WithClickFunction(() => {
                            UpdateCommandCostAmt(b, 1);
                        })
                        .WithClick2Function(() => {
                            UpdateCommandCostAmt(b, -1);
                        });
                    commandBlocks[cmd.Key] = b;
                }
                b.UpdateIcon(enabled ? IconId.CCheck : IconId.CCross);
                b.UpdateTitle(enabled ? cmdName : Texts.Strike(cmdName));
                b.UpdateValue(cost);
                res.Add(b);
            }
        }

        private void AddRoleAutoAddInput(List<UDB> res, TwitchRole role) {
            UDB udb = null;
            udb = UDB.Create(this, UDBT.IAmtInput, IconId.CStockMarket, "Required Reputation")
                .WithIconClickFunction(() => {
                    D.Warn("icon click");
                })
                .WithText2("0 = disabled")
                .WithRange(0, int.MaxValue)
                .WithValueOf(GetRoleData(role).AddAtReputation)
                .WithValueChangeFunction((b, v) => {
                    int newV = Mathf.RoundToInt((float) v);
                    var rd = GetRoleData(role);
                    if (rd.AddAtReputation != newV) {
                        rd.AddAtReputation = newV;
                        UpdateRoleData(rd);
                    }
                })
                // right arrow
                .WithClickFunction(() => {
                    ChangeAddAtRepAmt(udb, role, 1);
                })
                // left arrow
                .WithClick2Function(() => {
                    ChangeAddAtRepAmt(udb, role, -1);
                });
                udb.WithTooltip("Role will be added to viewers who have this reputation amount");
            res.Add(udb);
        }

        private void ChangeAddAtRepAmt(UDB udb, TwitchRole role, int dir) {
            var isCtrl = The.Bindings.IsPressed(Input.ActionType.CtrlModifier);
            var isShift = The.Bindings.IsPressed(Input.ActionType.ShiftModifier);
            var isAlt = The.Bindings.IsPressed(Input.ActionType.AltModifier);
            var rd = GetRoleData(role);
            var amt = rd.AddAtReputation;
            // alt increments by 50%
            // ctrl increments by full
            // shift increments by 10%
            int change;
            if (isAlt) {
                change = dir * 60;
            } else if (isCtrl) {
                change = dir * 15;
            } else if (isShift) {
                change = dir * 30;
            } else {
                change = dir;
            }
            amt = Maths.Clamp(amt + change, 0, int.MaxValue);
            rd.AddAtReputation = amt;
            UpdateRoleData(rd);
            udb.UpdateValue(amt);
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

        private void AddToggle(List<UDB> res, string name,
                Func<bool> getValue, Action<bool> setValue, string tooltip = null) {
            UDB udb = null;
            udb = UDB.Create(this, UDBT.DBtn, null, name)
                .WithClickFunction(() => {
                    var curVal = !getValue();
                    setValue(curVal);
                    udb.UpdateIcon(curVal ? IconId.CCheck : IconId.CCross);
                });
            udb.UpdateIcon(getValue() ? IconId.CCheck : IconId.CCross);
            udb.WithTooltip(tooltip);
            res.Add(udb);
        }

        private void ShowSelectedViewer(List<UDB> res) {
            rightLabel = UDB.Create(this, UDBT.DLabel, null, "Viewer");
            res.Add(rightLabel);
            if (SelectedViewer == null) {
                rightLabel.NeedsListRebuild = true;
                return;
            }
            var v = SelectedViewer;
            sys.ApplyRoleChanges(v);
            var beingData = AddViewerUDB(res, v, true);
            var lastSeen = UDB.Create(this, UDBT.DText, IconId.CTime, "Last Seen")
                .WithText(v.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            res.Add(lastSeen);
            res.Add(UDBCache.Label("Reputation"));
            res.Add(UDB.Create(this, UDBT.DText, IconId.CUpgrade, "Total Gained").WithText(Units.Num(v.TotalReputation)));
            res.Add(UDB.Create(this, UDBT.DText, IconId.CUpgrade, "Current").WithText(v.ReputationText));
            res.Add(UDB.Create(this, UDBT.DBtn, IconId.WPlus, Texts.Green("+100"))
                .WithClickFunction(() => {
                    SelectedViewer.AddReputation(100);
                    RefreshViewer(SelectedViewer);
                }));
            res.Add(UDB.Create(this, UDBT.DBtn, IconId.WPlus, Texts.Green("+1000"))
                .WithClickFunction(() => {
                    SelectedViewer.AddReputation(1000);
                    RefreshViewer(SelectedViewer);
                }));
            res.Add(UDB.Create(this, UDBT.DBtn, IconId.WMinus, Texts.Red("-100"))
                .WithClickFunction(() => {
                    SelectedViewer.AddReputation(-100);
                    RefreshViewer(SelectedViewer);
                }));
            res.Add(UDB.Create(this, UDBT.DBtn, IconId.WMinus, Texts.Red("-1000"))
                .WithClickFunction(() => {
                    SelectedViewer.AddReputation(-1000);
                    RefreshViewer(SelectedViewer);
                }));
            res.Add(UDB.Create(this, UDBT.DBtnLong, IconId.WMinus, Texts.Red("Set to 0"))
                .WithLongPressFunction(() => {
                    SelectedViewer.Reputation = 0;
                    RefreshViewer(SelectedViewer);
                }));
            res.Add(UDBCache.Label("Flags"));
            foreach (var flag in TwitchSys.AllFlags)  {
                bool hasFlag = (v.Flags & flag) == flag;
                res.Add(UDB.Create(this, UDBT.DText, hasFlag ? IconId.CCheck : IconId.CCross, flag.ToString()));
            }
            res.Add(UDBCache.Label("Roles"));
            foreach (var role in TwitchRole.All) {
                bool hasRole = v.HasRole(role.Key);
                res.Add(UDB.Create(this, UDBT.DText, hasRole ? IconId.CCheck : IconId.CCross, role.Value.Id));
            }
            res.Add(UDBCache.Label("Recent Commands"));
            if (Arrays.IsEmpty(v.LastCommands)) {
                res.Add(UDBCache.Empty);
            } else {
                for (int i = v.LastCommands.Count - 1; i >= 0; i--) {
                    var cmd = v.LastCommands[i];
                    res.Add(UDB.Create(this, UDBT.DText,
                            GraphicsId.IconsColorTwitchGlitch, sys.CommandName(cmd, true))
                        .WithText(cmd.Time.ToLocalTime().ToShortTimeString()));
                }
            }
            if (!Arrays.IsEmpty(v.CommandCounts)) {
                res.Add(UDBCache.Label("Command Counts"));
                foreach (var c in TwitchSys.CommandMap.OrderBy(kv => v)) {
                    if (v.CommandCounts.TryGetValue(c.Key, out var cnt)) {
                        res.Add(UDB.Create(this, UDBT.DText,
                                GraphicsId.IconsColorTwitchGlitch, sys.Settings.AddPrefix(c.Value, true))
                            .WithText(Units.XNum(cnt)));
                    }
                }
            }
            res.Add(UDBCache.Label(T.UiGroupManagement));

            var qIdx = sys.Queue.NumberOf(v);
            if (qIdx != -1) {
                UDB qUdb = null;
                res.Add(qUdb = UDB.Create(this, UDBT.DTextBtn, IconId.CCount, "In Queue")
                    .WithText($"#{qIdx}")
                    .WithText2(T.Remove)
                    .WithClickFunction(() => {
                        sys.Queue.TryRemove(v);
                        RebuildViewers();
                        qUdb.NeedsListRebuild = true;
                    }));
            }

            var assignTitle = beingData == null ? "Assign Being" : "Reassign Being";
            res.Add(UDB.Create(this, UDBT.DBtn, IconId.CPersona, assignTitle)
                .WithClickFunction(() => {
                    ctxMode = CtxMode.AssignBeing;
                    S.Sig.ShowContextMenu.Send(this);
                }));
            if (beingData != null) {
                res.Add(UDB.Create(this, UDBT.DBtn, IconId.CCross, "Unassign Being")
                    .WithClickFunction(() => {
                        if (beingData.Mind.Name.FullName.Contains(v.Name)) {
                            beingData.Mind.Name.ParseName($"Ex {v.Name}");
                            beingData.UI.AfterRename();
                        }
                        sys.GameData.BeingIds.Remove(v.Id);
                        RefreshViewer(v);
                        RebuildViewers();
                    }));
            }
            res.Add(UDB.Create(this, UDBT.DBtnLong, IconId.CWarning, "Delete Viewer")
                .WithTooltip("This will delete the viewer reputation, roles and data from the current game")
                .WithLongPressFunction(() => {
                    TwitchSys.GlobalData.Viewers.Remove(v.Id);
                    sys.Queue.TryRemove(v);
                    RebuildViewers();
                    SelectedViewer = null;
                    rightLabel.NeedsListRebuild = true;
                }));

        }

        public void RefreshViewer(TwitchViewer viewer) {
            if (SelectedViewer == viewer) {
                if (rightLabel != null && rightLabel.IsShowing) {
                    rightLabel.NeedsListRebuild = true;
                }
            }
        }

        public void RebuildViewers() {
            if (leftLabel?.IsShowing == true) {
                leftLabel.NeedsListRebuild = true;
            }
        }

        private void UpdateCommandCostAmt(UDB b, int dir) {
            var isCtrl = The.Bindings.IsPressed(Input.ActionType.CtrlModifier);
            var isShift = The.Bindings.IsPressed(Input.ActionType.ShiftModifier);
            var isAlt = The.Bindings.IsPressed(Input.ActionType.AltModifier);
            // alt increments by 15
            // ctrl increments by 30
            // shift increments by 60
            int change;
            if (isAlt) {
                change = dir * 15;
            } else if (isCtrl) {
                change = dir * 30;
            } else if (isShift) {
                change = dir * 60;
            } else {
                change = dir;
            }
            int amount = ((int) b.Value) + change;
            if (amount < 0) {
                amount = 0;
            }
            b.UpdateValue(amount);
        }
    }
}
#endif
