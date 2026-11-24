#if !PRODUCTION_BUILD
// #define DEBUG_OPERATOR
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class OperatableDeviceTab : BaseEntityTab {
        public const string TabId = "OperatableDevice";
        private const string SubId = "OperatableDevice";

        [SelfInit]
        private static void Register() {
            RegisterTab(new OperatableDeviceTab());
        }

        private Tile tile;
        private OperatableDeviceComp operatable;
        private GameState S;
        private WBase assignWidget;

        public override string Id => TabId;
        public override int Order => 30;
        public override string TabName => T.NounOperator;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<OperatableDeviceComp>(out var op)) { return false; }
            if (op.IsAutomated) { return false; }
            this.tile = tile;
            operatable = op;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            operatable.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            int gIdx = 0;

            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            LoadRequirements(block, ref idx);

            if (!operatable.IsAutomated) {
                var assignBlock = UISpawn.Block(lifetime, group, gIdx++);
                idx = 0;

                var header = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, assignBlock.Container, idx++);
                header.Load(T.NounAssignedOperators);

                LoadAssignedOperators(assignBlock.Container, ref idx);

                if (SubPanelWidget.IsOpenFor == SubId && assignWidget != null) {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                    ShowAssignSubPanel(assignWidget);
                    var btn = assignWidget;
                    Coroutines.OnNextFrame(() => {
                        var sp = S.UIQuery.GetEntitySubPanel.Ask();
                        sp.RepositionWidget(btn, connectorOnly: false);
                    });
                }
            }


            #if DEBUG_OPERATOR
            if (tile.Transform.IsRotatable || operatable.DebugOffsets != null) {
                var debugBlock = UISpawn.Block(lifetime, group, gIdx++);
                int dIdx = 0;
                var debugHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, debugBlock.Container, dIdx++);
                debugHeader.Load("Debug: Operator Position");

                var facingRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, debugBlock.Container, dIdx++);
                facingRow.Load(IconId.CInfo, "Facing", tile.Transform.Rotation.ToString());

                var offsets = operatable.DebugOffsets;
                if (offsets != null) {
                    for (int i = 0; i < offsets.Length; i++) {
                        var o = offsets[i];
                        var isActive = (int) tile.Transform.Rotation == i;
                        var offsetRow = UISpawn.Widget<WIconText2>(lifetime,
                            UIPrefabId.WContent2Text2, debugBlock.Container, dIdx++);
                        offsetRow.Load(isActive ? IconId.CCheck : IconId.CCross,
                            $"Offset_{i}", o.ToString());
                    }
                }
            }
            #endif

            return true;
        }

        private void LoadRequirements(WGroup block, ref int idx) {
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.NounRequirements);

            var jobType = operatable.JobType;
            var skillRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            skillRow.Load(jobType.IconId ?? IconId.CInfo, T.RequiredSkill, jobType.NameT);

            var required = operatable.RequiredAbilities;
            var rows = new List<(string icon, string name)>();
            for (int i = 0; i < required.Length; i++) {
                var ability = Ability.Get(required[i]);
                if (ability == null) { continue; }
                rows.Add((ability.IconId ?? IconId.CInfo, ability.NameT));
            }
            StatListUI.AddExpandableList(lifetime, block, ref idx, T.RequiredAbilities, rows,
                IconId.COperate);
        }

        private void LoadAssignedOperators(RectTransform container, ref int idx) {
            assignWidget = null;

            if (Arrays.IsEmpty(operatable.AllowedMindIdsActual)) {
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, container, idx++);
                assignWidget = w;
                w.Load(IconId.CBodyGeneric, T.AdjUnassigned,
                    Texts.Grey(T.SelectWorker), () => {
                    ShowAssignSubPanel(w);
                });
                var unW = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2TextBlock, container, idx++);
                unW.Load(Texts.Italic(Texts.Grey(T.OperatableNotAssignedTip)));
                return;
            }

            for (int i = 0; i < operatable.AllowedMindIdsActual.Length; i++) {
                LoadAssignedOperator(container, ref idx, operatable.AllowedMindIdsActual[i]);
            }

            var addBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WContent2ButtonSimple, container, idx++);
            assignWidget = addBtn;
            addBtn.Load(Texts.Italic(T.SelectWorker), () => {
                ShowAssignSubPanel(addBtn);
            });
        }

        private void LoadAssignedOperator(RectTransform container, ref int idx,
                uint mindId) {
            var w = UISpawn.Widget<WContentButtonWithX>(lifetime,
                UIPrefabId.WContent2ButtonX, container, idx++);
            var beingData = FindBeingByMindId(mindId);
            if (beingData != null) {
                var canOperate = CanAssignBeing(beingData, false);
                var displayName = BeingUtils.DecoratedBeingName(beingData, false);
                if (!canOperate) {
                    displayName = $"{GlyphMap.SpriteWarning} {Texts.Strike(displayName)}";
                }
                w.Button.Load(null, displayName, () => {
                    var currentBeingData = FindBeingByMindId(mindId);
                    if (currentBeingData?.Being != null) {
                        A.S.Sig.FollowEntity.Send(currentBeingData.Being);
                    }
                });
                beingData.Avatar.Load(w.Button.Image);
            } else {
                w.Button.Load(IconId.COperate, GetMindName(mindId), () => { });
                w.Button.SetInteractable(false);
            }
            UIUtils.AddTipFunc(lifetime, w, () => {
                var currentBeingData = FindBeingByMindId(mindId);
                if (!CanAssignBeing(currentBeingData, false)) {
                    return T.OperatableWorkerUnable_V1.F(GetMindName(mindId));
                }
                var tip = currentBeingData.Mind.Jobs.Tooltip(T.NounJobs,
                    new int[] { operatable.JobType.IdHash });
                return tip;
            });
            w.XButton.Load(() => {
                RemoveAllowedMindId(mindId);
            });
        }

        private void ShowAssignSubPanel(WBase forWidget) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.SelectWorker);

            int gIdx = 0;
            var block = UISpawn.Block(l, subPanel.Container, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.AdjAvailable);

            int added = 0;
            foreach (var beingData in GetAssignableOperators()) {
                var w = UISpawn.Widget<WContentButton2>(l,
                    UIPrefabId.WContent2Button2, block.Container, idx++);
                w.Item.Load(null, BeingUtils.DecoratedBeingName(beingData, false), () => {
                    if (beingData.Being != null) {
                        A.S.Sig.FollowEntity.Send(beingData.Being);
                    }
                });
                w.Button.Load(T.VerbAssign, () => {
                    AddAllowedMindId(beingData.Mind.Id);
                });
                UIUtils.AddTipFunc(l, w, () => {
                    return beingData.Mind.Jobs.Tooltip(T.NounJobs,
                        new int[] { operatable.JobType.IdHash });
                    });
                beingData.Avatar.Load(w.Item.Image);
                added++;
            }

            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }

            subPanel.Show(forWidget, SubId);
        }

        private IEnumerable<BeingData> GetAssignableOperators() {
            var allBeings = S.Query.AllBeingData.Ask();

            return allBeings.Values
                .Where(b => CanAssignBeing(b, true))
                .OrderByDescending(b => b.Mind.Jobs.LevelOf(operatable.JobType.IdHash))
                .ThenBy(b => b.Mind.Name.ShortName);
        }

        private bool CanAssignBeing(BeingData beingData, bool skipAssigned) {
            if (beingData == null || beingData.Mind == null) {
                return false;
            }
            if (!beingData.EffectiveFaction.IsPlayer) {
                return false;
            }
            if (beingData.HasStateFlag(BeingState.Dead)) {
                return false;
            }
            if (beingData.Body.IsIncapacitated) {
                return false;
            }
            if (beingData.Body.HasFlag(BodyParts.BodyFlags.Missing)) {
                return false;
            }
            if (!operatable.IsAbleToOperate(beingData)) {
                return false;
            }
            if (skipAssigned) {
                return !Arrays.ContainsSafe(operatable.AllowedMindIdsActual, beingData.Mind.Id);
            }
            return true;
        }

        private BeingData FindBeingByMindId(uint mindId) {
            var mind = S.Query.FindMind.Ask(mindId);
            return mind?.Owner;
        }

        private string GetMindName(uint mindId) {
            var mind = S.Query.FindMind.Ask(mindId);
            if (mind != null) {
                return mind.Name.FullNameOrNameless;
            }
            return $"{T.Unknown} [{mindId}]";
        }

        private void AddAllowedMindId(uint mindId) {
            if (Arrays.ContainsSafe(operatable.AllowedMindIdsActual, mindId)) {
                return;
            }

            if (Arrays.IsEmpty(operatable.AllowedMindIdsActual)) {
                operatable.SetAllowedMindIds(new[] { mindId });
            } else {
                var ids = new uint[operatable.AllowedMindIdsActual.Length + 1];
                Array.Copy(operatable.AllowedMindIdsActual, ids, operatable.AllowedMindIdsActual.Length);
                ids[^1] = mindId;
                operatable.SetAllowedMindIds(ids);
            }

            ReloadTab();
        }

        private void RemoveAllowedMindId(uint mindId) {
            operatable.TryRemoveAllowedMindId(mindId);
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;

            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime, UIPrefabId.WContent4Priority, group.Container, idx++);
            prio.Up.Load(IconId.WArrowUp, () => {
                operatable.SetPriority(Math.Clamp(operatable.GoalPriority + 1, 1, 5));
                prio.Texts.SetText2(Units.XOutOfY(operatable.GoalPriority, 5));
            });
            prio.Down.Load(IconId.WArrowDown, () => {
                operatable.SetPriority(Math.Clamp(operatable.GoalPriority - 1, 1, 5));
                prio.Texts.SetText2(Units.XOutOfY(operatable.GoalPriority, 5));
            });
            prio.Texts.Load(T.TaskPriority, Units.XOutOfY(operatable.GoalPriority, 5));
            UIUtils.AddTip(lifetime, prio, T.JobPriorityHint);

            if (operatable.CanKeepOperator) {
                var keepBtn = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WContent4Toggle, group.Container, idx++);
                keepBtn.Load(T.KeepOperator, operatable.KeepOperator, (v) => {
                    operatable.ToggleKeepOperator();
                });
                UIUtils.AddTip(lifetime, keepBtn, T.KeepOperatorTip);
            }
            return true;
        }

        protected override void Cleanup() {
            assignWidget = null;
            tile = null;
            operatable = null;
            S = null;
        }
    }
}
