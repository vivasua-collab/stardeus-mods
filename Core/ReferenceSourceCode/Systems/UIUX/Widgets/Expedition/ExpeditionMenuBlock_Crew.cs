using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ExpeditionMenuBlock_Crew : ExpeditionMenuBlock {
        public override int Order => 10;
        private Lifetime lifetime;
        private Lifetime crewLife;
        private Lifetime crewPopupContentLife;
        private Lifetime crewListLife;
        private WGroup crewBlock;
        private readonly int[] importantJobsMining = new int[] {
            JobTypeIdH.Mining
        };
        private readonly int[] importantJobsMiningExpedition = new int[] {
            JobTypeIdH.Mining,
            JobTypeIdH.Expedition,
        };

        public ExpeditionMenuBlock_Crew(ExpeditionsWidget parent,
            GameState s) : base(parent, s) { }

        public override void Load(int siblingIdx, ShuttleComp shuttle, Expedition exp) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Exp.Crew");
            crewBlock = UISpawn.Block(lifetime, parent.SubPanel.Container, siblingIdx);
            if (ctx.IsSetup) {
                ctx.TargetSO.Bind(lifetime, (_) => {
                    ctx.AutofillCrew();
                    LoadCrewChoiceList();
                    ctx.OnCrewChanged.Invoke();
                });
            } else {
                LoadExistingCrew(exp);
            }
        }

        private void LoadExistingCrew(Expedition exp) {
            if (crewListLife != null) {
                crewListLife.Dispose();
            }
            if (exp?.Crew == null) {
                // Defensive: should not happen now that ExpeditionContext only commits
                // Expedition on an actual launch, but avoids crashing the widget if it does.
                return;
            }
            crewListLife = lifetime.CreateChild("Exp.Crew.List");
            int idx = 0;
            var block = crewBlock;
            var header = UISpawn.Widget<WText>(crewListLife,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.ExpeditionCrew);
            foreach (var being in exp.Crew) {
                var w = UISpawn.Widget<WButtonIconText2>(crewListLife,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                w.Load(null, CrewMemberName(ctx.IsExploration, being, false), null, () => {
                    BeingUtils.ShowProfile(being);
                });
                being.Avatar.Load(w.Image);

                being.Mind.Health?.Sanity.BFloat.Bind(crewListLife, (v) => {
                    w.SetText2(being.Mind.Health.SanityPctWithColor);
                });
            }
        }

        private void LoadCrewChoiceList() {
            if (crewListLife != null) {
                crewListLife.Dispose();
            }
            crewListLife = lifetime.CreateChild("Exp.Crew.List");
            int idx = 0;
            var block = crewBlock;
            var header = UISpawn.Widget<WText>(crewListLife,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.ExpeditionCrew);
            if (ctx.crewCandidates != null) {
                foreach (var being in ctx.crewCandidates) {
                    var w = UISpawn.Widget<WButtonIconText2>(crewListLife,
                        UIPrefabId.WContent2Button, block.Container, idx++);
                    bool isRequired = ctx.RequiredCrewMindIds?.Contains(being.Mind.Id) == true;
                    string displayName = CrewMemberName(ctx.IsExploration, being, isRequired);
                    if (isRequired) {
                        w.Load(null, displayName, null, () => {
                            UISounds.PlayActionDenied();
                        });
                    } else {
                        w.Load(null, displayName, null, () => {
                            ctx.crewCandidates.Remove(being);
                            if (ctx.crewCandidates.Count == ctx.Shuttle.Capacity - 1) {
                                LoadCrewChoiceList();
                            } else {
                                w.Dispose();
                            }
                            ctx.OnCrewChanged.Invoke();
                        });
                    }
                    being.Avatar.Load(w.Image);
                    UIUtils.AddTipFunc(crewListLife, w, () => {
                        return being.Mind.Jobs.Tooltip(
                            being.Being?.GetComponent<MentalHealthComp>()?.Tooltip(false),
                            ctx.IsExploration ?
                                importantJobsMiningExpedition : importantJobsMining);
                    });
                    being.Mind.Health?.Sanity.BInt.Bind(crewListLife, (v) => {
                        w.SetText2(being.Mind.Health.SanityPctWithColor);
                    });
                }
            }

            if (ctx.crewCandidates == null ||
                ctx.crewCandidates.Count < ctx.Shuttle.Capacity) {
                var w2 = UISpawn.Widget<WButtonText>(crewListLife, UIPrefabId.WContent2ButtonSimple, block.Container, idx);
                w2.Load(Texts.Italic(T.AddCrewMember), ShowCrewChoicePopup);
            }
        }

        private void ShowCrewChoicePopup() {
            if (crewLife?.IsDisposed == false) {
                return;
            }
            // Slow the clock while the player is choosing crew so candidates don't go stale
            // (e.g. become tired) between being picked and the expedition actually launching.
            parent.SlowClockToMinSpeed();
            LoadCrewChoiceList();
            if (crewLife != null) {
                crewLife.Dispose();
            }
            crewLife = lifetime.CreateChild("Exp.Crew.Add");
            var choiceWidget = UISpawn.Widget<WPanel>(
                    crewLife, UIPrefabId.WListChoicePopup, UIPoolRoot.PopupContainer, -1);
            choiceWidget.ShowFooter(false);
            choiceWidget.Load(T.SelectCrew, () => {
                crewLife.Dispose();
                LoadCrewChoiceList();
            });
            UIUtils.AfterShowPopup(choiceWidget.Rect);
            LoadCrewChoicePopupContent(choiceWidget);
        }

        private void LoadCrewChoicePopupContent(WPanel choiceWidget) {
            if (crewPopupContentLife != null) {
                crewPopupContentLife.Dispose();
            }
            crewPopupContentLife = crewLife.CreateChild("Exp.Crew.Add.Content");
            Being[] aliveBeings;
            if (ctx.IsExploration) {
                aliveBeings = S.Beings.Alive.OrderByDescending(
                    b => b.Data.Mind.Jobs.LevelOf(JobTypeIdH.Expedition)
                ).ThenByDescending(b => b.Data.Mind.Jobs.LevelOf(JobTypeIdH.Mining))
                    .ToArray();

            } else {
                aliveBeings = S.Beings.Alive.OrderByDescending(
                    b => b.Data.Mind.Jobs.LevelOf(JobTypeIdH.Mining)
                ).ToArray();
            }
            Caches.BeingDataList.Clear();
            unreachable.Clear();
            int countAdded = 0;
            var selectedBeings = ctx.crewCandidates ??= new List<BeingData>();

            int bIdx = 0;
            var blockAvail = UISpawn.Block(crewPopupContentLife, choiceWidget.Content, bIdx++);
            var idx = 0;
            var choiceHeader = UISpawn.Widget<WText>(crewPopupContentLife, UIPrefabId.WContent2Header,
                blockAvail.Container, idx++);
            choiceHeader.Load(T.AdjAvailable);

            for (int i = 0; i < aliveBeings.Length; i++) {
                ListAvailableMember(aliveBeings, ref countAdded, selectedBeings, blockAvail, ref idx, i);
            }
            if (countAdded == 0) {
                var w = UISpawn.Widget<WIconText>(crewPopupContentLife,
                    UIPrefabId.WContent2Text, blockAvail.Container, idx++);
                w.Load(IconId.CInfo, T.Empty);
            }
            if (Caches.BeingDataList.Count > 0) {
                var blockTired = UISpawn.Block(crewPopupContentLife, choiceWidget.Content, bIdx++);
                idx = 0;
                var tiredHeader = UISpawn.Widget<WText>(crewPopupContentLife, UIPrefabId.WContent2Header,
                    blockTired.Container, idx++);
                tiredHeader.Load(T.AdjectiveTired);

                foreach (var being in Caches.BeingDataList) {
                    ListTiredMember(being, blockTired, ref idx);
                }
            }
            if (unreachable.Count > 0) {
                var blockUnreachable = UISpawn.Block(crewPopupContentLife, choiceWidget.Content, bIdx++);
                idx = 0;
                var unreachableHeader = UISpawn.Widget<WText>(crewPopupContentLife,
                    UIPrefabId.WContent2Header, blockUnreachable.Container, idx++);
                unreachableHeader.Load(T.ExpeditionCrewCannotReach);
                foreach (var being in unreachable) {
                    ListUnreachableMember(being, blockUnreachable, ref idx);
                }
                unreachable.Clear();
            }

            // The clock may have been unpaused while this popup was open, making the
            // Available/Tired split stale. Let the player rebuild it on demand.
            var blockRefresh = UISpawn.Block(crewPopupContentLife, choiceWidget.Content, bIdx++);
            var refreshBtn = UISpawn.Widget<WButtonText>(crewPopupContentLife,
                UIPrefabId.WContent2ButtonSimple, blockRefresh.Container, 0);
            refreshBtn.Load(Texts.Italic(T.Refresh), () => {
                parent.SlowClockToMinSpeed();
                LoadCrewChoicePopupContent(choiceWidget);
                UIUtils.RebuildScroll(choiceWidget.Scroll, true);
                Coroutines.OnNextFrame(UIHoverFix.RefreshHover);
            });
        }

        // Picking somebody who has no path to the shuttle used to be allowed, and the
        // expedition then failed at launch with no explanation. They are still listed, so the
        // player sees why they are missing, but choosing one only explains the problem.
        private readonly List<BeingData> unreachable = new();

        private bool CanReachShuttle(Being being) {
            return ctx.Shuttle == null || EntityUtils.CanReach(being, ctx.Shuttle.InPort);
        }

        private void ListUnreachableMember(BeingData being, WContentBlock block, ref int idx) {
            var w = UISpawn.Widget<WButtonIconText2>(crewPopupContentLife,
                UIPrefabId.WContent2Button, block.Container, idx++);
            w.Load(null, CrewMemberName(ctx.IsExploration, being, false), null, () => {
                UISounds.PlayActionDenied();
                S.Sig.ShowErrorMessage.Send(T.ExpeditionCrewCannotReachError_V2.F(
                    being.Mind.Name.ShortName, ctx.Shuttle.Tile.Definition.NameT));
            });
            being.Avatar.Load(w.Image);
            if (being.Mind.Health != null) {
                w.SetText2(being.Mind.Health.SanityPctWithColor);
            }
        }

        private void ListTiredMember(BeingData being, WContentBlock blockTired, ref int idx) {
            var w = UISpawn.Widget<WButtonIconText2>(crewPopupContentLife,
                UIPrefabId.WContent2Button, blockTired.Container, idx++);
            w.Load(null, CrewMemberName(ctx.IsExploration, being, false), null, () => {
                BeingUtils.SelectFocused(being);
            });
            var avatar = S.Query.GetBeingAvatar.Ask(being);
            being.Avatar.Load(w.Image);
            UIUtils.AddTipFunc(crewPopupContentLife, w, () => {
                return being.Mind.Jobs.Tooltip(
                being.Being?.GetComponent<MentalHealthComp>()?.Tooltip(false),
                    ctx.IsExploration ?
                        importantJobsMiningExpedition : importantJobsMining);
            });
            if (being.Mind.Health != null) {
                w.SetText2(being.Mind.Health.SanityPctWithColor);
            }
        }

        private void ListAvailableMember(Being[] aliveBeings, ref int countAdded,
                List<BeingData> selectedBeings, WContentBlock blockAvail,
                ref int idx, int i) {
            Being being = aliveBeings[i];
            if (selectedBeings.Contains(being.Data)) { return; }
            if (!BeingUtils.CanBeSentOnExpedition(being, ctx.IsExploration)) {
                return;
            }
            if (S.Sys.Expedition.IsOnExpedition(being.Data, ctx.Expedition)) {
                return;
            }
            if (BeingUtils.IsTired(being.Data)) {
                Caches.BeingDataList.Add(being.Data);
                return;
            }
            if (!CanReachShuttle(being)) {
                unreachable.Add(being.Data);
                return;
            }
            var w = UISpawn.Widget<WButtonIconText2>(crewPopupContentLife,
                UIPrefabId.WContent2Button,
                blockAvail.Container, idx++);
            w.Load(null, CrewMemberName(ctx.IsExploration, being.Data, false), null, () => {
                EntityUtils.HideEntityLink();
                selectedBeings.Add(being.Data);
                if (selectedBeings.Count >= ctx.Shuttle.Capacity ||
                        !HasAvailableMemberToAdd(selectedBeings)) {
                    crewLife.Dispose();
                } else {
                    w.Dispose();
                }
                LoadCrewChoiceList();
                ctx.OnCrewChanged.Invoke();
            });
            if (being.Data.Mind.Health != null) {
                w.SetText2(being.Data.Mind.Health.SanityPctWithColor);
            }
            being.Data.Avatar.Load(w.Image);
            UIUtils.AddTipFunc(crewPopupContentLife, w, () => {
                return being.Data.Mind.Jobs.Tooltip(
                    being.GetComponent<MentalHealthComp>()?.Tooltip(false),
                        ctx.IsExploration ?
                            importantJobsMiningExpedition : importantJobsMining);

            });
            countAdded++;
        }

        private bool HasAvailableMemberToAdd(List<BeingData> selectedBeings) {
            foreach (var being in S.Beings.Alive) {
                if (selectedBeings.Contains(being.Data)) {
                    continue;
                }
                if (!BeingUtils.CanBeSentOnExpedition(being, ctx.IsExploration)) {
                    continue;
                }
                if (S.Sys.Expedition.IsOnExpedition(being.Data, ctx.Expedition)) {
                    continue;
                }
                if (BeingUtils.IsTired(being.Data)) {
                    continue;
                }
                if (!CanReachShuttle(being)) {
                    continue;
                }
                return true;
            }
            return false;
        }

        public static string CrewMemberName(bool isExploration, BeingData data, bool isQuest = false) {
            byte level;
            string job;
            bool can;
            var name = data.Mind.Name.ShortName;
            if (isQuest) {
                name = $"{name} [{Texts.Accented(T.NounQuest)}]";
            } else {
                if (isExploration) {
                    job = T.JobTypeExpedition;
                    can = data.Mind.Jobs.CanDo(JobTypeIdH.Expedition, out level);
                } else {
                    job = T.JobTypeMining;
                    if (!data.Mind.Jobs.CanDo(JobTypeIdH.Mining, out level)) {
                        name = T.ExpCrewNameNoMining_V2.F(name, Texts.TMPColorYellow);
                        return BeingUtils.DecoratedBeingName(data, false, name);
                    }
                    can = true;
                }
                string lvl;
                if (can) {
                    lvl = $"[{job}: {level}]";
                } else {
                    lvl = $"[{job}: {Texts.RedMinus}]";
                }
                lvl = Texts.Grey(lvl);
                name = $"{name} {lvl}";
            }

            return BeingUtils.DecoratedBeingName(data, false, name);
        }
    }
}
