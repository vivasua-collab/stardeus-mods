using System;
using Game.Constants;
using Game.Data;
using Game.Components;
using KL.Utils;
using UnityEngine;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using Game.Systems.UIUX;
using System.Collections.Generic;
using Game.Systems.Creatures;
using Game.Systems.AI;
using System.Linq;
using System.Transactions;

namespace Game.Expeditions {
    public sealed class ExpeditionStagePrep : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.Prep, () => {
                return new ExpeditionStagePrep();
            });
        }
        public override int StageType => ExpeditionStageTypeH.Prep;
        private readonly HashSet<uint> hailedMembers = new();
        public override string Title => T.ExpStagePrep;

        public override string MiniWidgetStatusIcon => IconId.CTime;

        public ExpeditionStagePrep() {
            MiniWidgetText.Set(T.VerbBoarding);
        }
        public override bool CanCallBack(Expedition exp) {
            return false;
        }

        public override bool IsStageCancellable => true;
        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (!Ready.Game) {
                // Too early, let's wait another tick
                return this;
            }
            if (exp.Shuttle.Tile.Health.IsDamaged) {
                WPopup.Create(false, IconId.CWarning, T.ExpeditionAborted, T.ExpeditionAbortedShuttleDamaged).Spawn();
                exp.S.Sys.Expedition.AbortExpedition(exp);
                return this;
            }
            if (!exp.StageData.GetBool("Prep.HasHailedCrew", false)) {
                HailCrew(exp);
                return this;
            }
            if (!IsCrewAssembled(exp, out var present, out var total)) {
                if (exp.IsDone) { return this; }
                // MiniWidgetText.Set(Units.XOutOfY(present, total));
                exp.Shuttle.ShowInfoIcon(IconId.CTime, $"{T.WaitingForWorker} ({Units.XOutOfY(present, total)})");
                return this;
            }
            exp.Shuttle.HideInfoIcon();
            exp.Shuttle.ShowDustFX();
            return ExpeditionStages.Find(ExpeditionStageTypeH.Takeoff);
        }

        private bool IsCrewAssembled(Expedition exp, out int present, out int total) {
            if (Arrays.IsEmpty(exp.Crew) && exp.Shuttle?.HasAutoPilot != true) {
                var msg = T.ExpeditionLaunchAborted_V1.F(exp.Shuttle?.Name ?? T.Unknown);
                The.Dialog.ShowSimple(IconId.CComputing, msg);
                exp.S.Sys.Expedition.AbortExpedition(exp);
                present = 0;
                total = 0;
                return false;
            }
            bool canLaunch = true;
            present = 0;
            for (int i = exp.Crew.Count - 1; i >= 0; i--) {
                BeingData member = exp.Crew[i];
                if (CheckCrewMemberInShuttle(exp, member)) {
                    present++;
                } else {
                    canLaunch = false;
                    if (exp.IsDone) {
                        // A failed member aborted the whole expedition, stop touching it
                        break;
                    }
                }
            }
            total = exp.Crew.Count;
            // Everyone is inside the shuttle
            return canLaunch;
        }

        private bool CheckCrewMemberInShuttle(Expedition exp, BeingData member) {
            if (member.HasStateFlag(BeingState.Expedition)) {
                if (member.Being != null && (exp.Shuttle == null || member.Being.PosIdx != exp.Shuttle.InPort)) {
                    // no dude, you're not
                    member.StateFlags &= ~BeingState.Expedition;
                    member.StateFlags &= ~BeingState.Hidden;
                } else {
                    return true;
                }
            } else {
                var b = member.Being;
                if (b == null) {
                    // member gone?
                    exp.S.Sig.FailedToEnterShuttle.Send(member, null);
                } else if (b.AIAgent.CurrentAction?.IdH != AIActionEnterShuttle.ActIdH
                        && b.AIAgent.Goal?.JobIdH != JobTypeIdH.Expedition) {
                    // did we wait for too long?
                    if (G.Ticks - exp.StartTick > Consts.TicksPer12Hours) {
                        b.S.Sig.FailedToEnterShuttle.Send(member, null);
                    }

                    // member doing something else?
                    hailedMembers.Remove(member.Id);
                    if (!HailCrewMember(exp, member)) {
                        b.S.Sig.FailedToEnterShuttle.Send(member, null);
                    }
                }
            }
            return false;
        }

        private bool HailCrewMember(Expedition exp, BeingData worker) {
            if (hailedMembers.Contains(worker.Id)) { return true; }
            if (!exp.Crew.Contains(worker)) { return false; }

            if (worker.Body.IsIncapacitated) {
                exp.S.Sig.FailedToEnterShuttle.Send(worker, T.BeingStateIncapacitated);
                return false;
            }

            var b = worker.Being;
            if (b == null || !b.IsActive || b.IsDead) {
                D.Err("Hailing a crew member that has null / inactive / dead being! {0} | {1} | {2}",
                    worker, worker.Body, b);
                exp.S.Sig.FailedToEnterShuttle.Send(worker, null);
                return false;
            }
            if (b.AIAgent.Goal != null) {
                b.AIAgent.DropGoal();
            }
            if (b.AIAgent.Goal != null) {
                D.Err("Failed to drop goal for expedition member, marking as failed to enter shuttle: {0}", worker);
                b.S.Sig.FailedToEnterShuttle.Send(worker, null);
                return false;
            }

            if (b.AIAgent.DirectControl.IsUnderDirectControl) {
                D.Err("Disabling direct control for expedition member: {0}", worker);
                b.AIAgent.DirectControl.SetDirectControl(false);
            }

            var goal = AIGoal.DirectOrder(JobTypeIdH.Expedition,
                IconId.CEnterSpaceObject, T.GoToShuttle, b.AIAgent, G.Ticks)
                    .WithVar("ShuttleId", exp.Shuttle.EntityId)
                    .WithWorldToDesired(AIVarsH.IsInShuttle, false, true);
            if (b.S.Query.ForceGoal.Ask(goal, false)) {
                goal.Flags |= AIGoalFlags.NonRetryable;
                b.AIAgent.Goals.Add(goal);
                hailedMembers.Add(worker.Id);
                return true;
            } else {
                b.S.Sig.FailedToEnterShuttle.Send(worker, null);
                return false;
            }
        }

        private void HailCrew(Expedition exp) {
            for (int i = exp.Crew.Count - 1; i >= 0; i--) {
                BeingData worker = exp.Crew[i];
                HailCrewMember(exp, worker);
            }

            exp.StageData.SetBool("Prep.HasHailedCrew", true);
        }
    }
}
