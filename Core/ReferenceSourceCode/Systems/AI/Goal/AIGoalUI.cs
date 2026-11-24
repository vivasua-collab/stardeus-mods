#if !PRODUCTION_BUILD
// #define DEBUG_TASKS
#endif
using Game.Constants;
using Game.Data;
using Game.Systems.Hints;
using Game.UI;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Areas;
using Game.Input;
using Game.Systems.UIUX;

namespace Game.Systems.AI {
    public sealed class AIGoalUI : IContextPopup {
        [ExecuteOnReload]
        private static void Cleanup() {
            lastPriorityAdjGoalId = 0;
        }
        public static AIGoalUI For(GameState S, AIGoal goal) {
            if (goal == null) {
                D.Err("Attempting to get UI for null goal!");
                return null;
            }
            return goal.UI ??= new AIGoalUI(S, goal);
        }

        public static bool IsFailureSuppressed(AIGoal goal) {
            return goal.Target is IAISuppressableGoalProvider supProv
                && supProv.ShouldSuppressGoalFailure(goal);
        }
        private AIGoalUI(GameState S, AIGoal goal) {
            this.S = S;
            this.goal = goal;
        }
        private readonly GameState S;
        private readonly AIGoal goal;
        public Entity Entity => goal.Target?.Entity ?? goal.Agent?.Being;
        private static uint lastPriorityAdjGoalId;
        private bool isOpen;

        // IContextPopup
        private static readonly int popupHash = Hashes.S("AIGoalPopup");
        public int UniquePopupHash => popupHash;
        public bool ShowInScreenCenter => false;
        public void OnPopupOpened() => isOpen = true;
        public void OnPopupClosed() => isOpen = false;

        private void JumpToPOI() {
            var poi = Entity;
            if (poi == null) {
                UISounds.PlayActionDenied();
                return;
            }
            The.R.Sig.SelectEntity.Send(poi);
        }

        private void HoverPOI(bool selected) {
            var poi = Entity;
            if (poi == null) {
                EntityUtils.HideEntityLink();
            } else {
                EntityUtils.ShowEntityLink(null, poi, selected);
            }
        }

        public string ShowInfoTip() {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, AIGoal.StateNameSimple(goal.State));
            if (goal.Agent != null) {
                sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, T.AssignedToWorker_V1.F(
                    Texts.Accented(goal.Agent.BeingData.Name)));
                if (goal.IsForceAssigned) {
                    sb.AppendFormat(" ({0})", Texts.Red(T.AdjForced));
                }
            }
            sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.TaskPriority, Texts.Accented(goal.GoalPriority));
            return sb.ToString();
        }

        public string Tooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            S.Query.LoadGoalTooltip.Ask(goal, true, sb);
            return sb.ToString();
        }

        private void ChangeGoalPriority(sbyte amt) {
            if (goal == null) { return; }
            if (AIGoal.IsCompleted(goal.State)) { return; }
            if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                amt = 5;
            }
            goal.AdjustPriority(G.Ticks, amt);
            if (goal.Id != lastPriorityAdjGoalId) {
                lastPriorityAdjGoalId = goal.Id;
                S.Sys.Hint.TrackAction(HintManagePriority.Id);
            }
        }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("AIGoal");
            int blockIdx = 0;

            // Block 0: goal info + activity state
            var mainBlock = UISpawn.Block(life, wContent, blockIdx++);
            int idx = 0;


            var actHeader = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, mainBlock.Container, idx++);
            actHeader.Load(T.UiGroupActivity);

            var goalRow = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, mainBlock.Container, idx++);
            goalRow.Load(goal.GoalIcon, goal.Name, goal.JobNameT);

            var taskStateRow = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, mainBlock.Container, idx++);
            taskStateRow.Load(IconId.CTime,
                AIGoal.StateName(goal.State),
                Units.TicksToTime(G.Ticks - goal.StateStartTick));
            goal.StateB.Bind(lifetime, (st) => {
                taskStateRow.Text2.SetText(
                    Units.TicksToTime(G.Ticks - goal.StateStartTick));
                taskStateRow.Text.SetText(AIGoal.StateName(st));
            });

            #if DEBUG_TASKS
            if (A.InstantBuild && goal.BestBid.Plan != null) {
                var planRow = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContent2Text2, mainBlock.Container, idx++);
                planRow.Load(IconId.CMagnifier, "Plan", goal.BestBid.Plan.Cost.ToString());
                UIUtils.AddTipFunc(life, planRow, () => D.DumpInstance(goal.BestBid.Plan));
            }
            #endif

            if (goal.Type == AIGoalType.Idle) {
                return T.TaskDetails;
            }

            bool isArchived = AIGoal.IsArchived(goal.State);
            var targetEntity = goal.Target?.Entity;

            if (targetEntity != null && targetEntity != goal.Agent?.Being) {
                var targetBlock = UISpawn.Block(life, wContent, blockIdx++);
                idx = 0;

                var targetHeader = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, targetBlock.Container, idx++);
                targetHeader.Load(T.NounTarget);

                if (!isArchived && S.Sys.Areas.HasUnreachableAreas &&
                        !S.Sys.Areas.IsAreaReachableAt(targetEntity.PosIdx)) {
                    var unreachRow = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent2Button, targetBlock.Container, idx++);
                    unreachRow.Load(IconId.CWarning, Texts.Red(T.AdRejUnreachable),
                        T.Details, () => DoorConnectionCheck.ShowUnreachableAreasPopup(S));
                    UIUtils.AddTip(life, unreachRow, T.UnreachableAreasDesc);
                }

                var capturedTarget = targetEntity;
                var targetRow = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, targetBlock.Container, idx++);
                targetRow.Load(null, targetEntity.Definition.NameT, null,
                    () => The.R.Sig.SelectEntity.Send(capturedTarget));
            }

            if (isArchived || goal.Type == AIGoalType.Personal) {
                return T.TaskDetails;
            }

            var workerBlock = UISpawn.Block(life, wContent, blockIdx++);
            idx = 0;

            var workerHeader = UISpawn.Widget<WText>(life,
                UIPrefabId.WContent2Header, workerBlock.Container, idx++);
            workerHeader.Load(T.NounWorker);

            if (goal.Agent != null) {
                if (goal.Agent.Being != Entity) {
                    var capturedAgent = goal.Agent;
                    var workerRow = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent2Button, workerBlock.Container, idx++);
                    workerRow.Load(null, capturedAgent.BeingData.Name, null,
                        () => The.R.Sig.SelectEntity.Send(capturedAgent.Being));
                    capturedAgent.BeingData.Avatar.Load(workerRow.Image);
                }
            } else {
                var noWorkerRow = UISpawn.Widget<WIconText>(life,
                    UIPrefabId.WContent2Text, workerBlock.Container, idx++);
                noWorkerRow.Load(IconId.CCross, T.WorkerNotAssigned);

                if (AIGoal.IsFailed(goal.State) && goal.FailReason != null) {
                    var failLabel = UISpawn.Widget<WText>(life,
                        UIPrefabId.WContent2Header, workerBlock.Container, idx++);
                    failLabel.Load(Texts.Red(T.OperationFailed));

                    var failReasonRow = UISpawn.Widget<WIconText>(life,
                        UIPrefabId.WContent2Text, workerBlock.Container, idx++);
                    failReasonRow.Load(IconId.CWarning, goal.FailReason);

                    var lastAttemptLabel = UISpawn.Widget<WText>(life,
                        UIPrefabId.WContent2Header, workerBlock.Container, idx++);
                    lastAttemptLabel.Load(Texts.Red(T.LastAttempt));

                    var failAgeRow = UISpawn.Widget<WIconText2>(life,
                        UIPrefabId.WContent2Text2, workerBlock.Container, idx++);
                    failAgeRow.Load(IconId.CTime,
                        Units.TicksAgo((int)(G.Ticks - goal.FailedAt)),
                        Units.XNum(goal.TimesFailedTotal));

                    if (goal.FailedAgentId != 0) {
                        var rejector = S.Query.FindBeingData.Ask(goal.FailedAgentId);
                        if (rejector != null) {
                            var capturedRej = rejector;
                            var rejRow = UISpawn.Widget<WButtonIconText2>(life,
                                UIPrefabId.WContent2Button, workerBlock.Container, idx++);
                            rejRow.Load(null, capturedRej.Name, null,
                                () => The.R.Sig.SelectEntity.Send(capturedRej.Being));
                            capturedRej.Avatar.Load(rejRow.Image);
                        }
                    }
                }
            }

            if (goal.Agent?.BeingData.EffectiveFaction.IsPlayer == false) {
                return T.TaskDetails;
            }

            bool needsPriority = goal.Type == AIGoalType.Colony;
            bool needsRetry = AIGoal.IsFailed(goal);
            bool needsCancel = goal.Type == AIGoalType.Colony || goal.JobIdH == JobTypeIdH.Cleaning;

            if (needsPriority || needsRetry || needsCancel) {
                int footerIdx = 0;

                if (needsPriority) {
                    var priorityWidget = UISpawn.Widget<WPriorityUpDown>(life,
                        UIPrefabId.WContent4Priority, wFooter.Container, footerIdx++);
                    priorityWidget.Texts.Load(T.TaskPriority, Units.XOutOfY(goal.GoalPriority, 5));
                    priorityWidget.Up.Load(IconId.WArrowUp, () => {
                        ChangeGoalPriority(+1);
                        priorityWidget.Texts.SetText2(Units.XOutOfY(goal.GoalPriority, 5));
                    });
                    priorityWidget.Down.Load(IconId.WArrowDown, () => {
                        ChangeGoalPriority(-1);
                        priorityWidget.Texts.SetText2(Units.XOutOfY(goal.GoalPriority, 5));
                    });
                    UIUtils.AddTip(life, priorityWidget, T.JobPriorityHint);
                }

                if (needsRetry) {
                    var retryBtn = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent4Button, wFooter.Container, footerIdx++);
                    retryBtn.Load(IconId.WRetry, T.AdvertRetry, null, OnClickGoalRetry);
                }

                if (needsCancel) {
                    var cancelBtn = UISpawn.Widget<WButtonIconText2>(life,
                        UIPrefabId.WContent4Button, wFooter.Container, footerIdx++);
                    cancelBtn.Load(IconId.CStop, T.CancelTask, null, OnClickGoalCancel);
                }

                wFooter.SetActiveFast(true);
            }

            return T.TaskDetails;
        }

        public void Rebuild() {
            if (isOpen) {
                S.UIQuery.ShowContextPopup.Ask(this, true);
            }
        }

        private void OnClickGoalCancel() {
            if (goal?.State != AIGoalState.Cancelled) {
                S.Sys.Hint.TrackAction(HintAdCancel.Id);
                AIGoal.Cancel(goal, G.Ticks);
                S.UIQuery.ShowContextPopup.Ask(this, false);
            }
        }

        private void OnClickGoalRetry() {
            if (goal == null) { return; }
            if (AIGoal.IsFailed(goal.State)) {
                if (AIGoal.TryRetry(goal, G.Ticks, true, true)) {
                    Rebuild();
                } else {
                    UISounds.PlayActionDenied();
                }
            }
        }
    }
}
