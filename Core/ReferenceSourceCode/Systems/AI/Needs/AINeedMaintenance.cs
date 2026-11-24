using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Sleep;
using Game.Systems.Stats;
using Game.Systems.Suggestions;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedMaintenance : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedMaintenance());
        }

        public override int[] RelatedStats => new int[] {
            StatIdH.MaintenanceRate
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CWrench2;
            priority = 4f;
            return data.Body.CanHeal;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = BodyIntegrity(data);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            ProcessMaintenance(data, ticks);
        }

        private void ProcessMaintenance(BeingData data, long ticks) {
            var agent = data.Being.AIAgent;
            bool hasGoal = agent.BB.TryNeedGoal(IdH, out var g);
            if (hasGoal && g.State == AIGoalState.InProgress) {
                // a raider on its way to our station that starts fleeing or leaving keeps
                // running (HostileSelfCare)
                if (HostileSelfCare.ShouldDropTrip(data)) {
                    AIGoal.Cancel(g, ticks);
                }
                // never touch goal that is in progress
                return;
            }
            if (!data.IsFriendly && !HostileSelfCare.MayStart(data)) {
                // the repair stations are the crew's: only a raider on the attack with
                // nobody around comes to use them (STA-1816)
                if (hasGoal && AIGoal.IsActive(g.State)) {
                    g.ChangeStateTo(AIGoalState.Failed, ticks);
                }
                return;
            }
            if (data.Body.CanHeal) {
                MaybeSuggestRepairStation(agent.S, data, ticks);
                if (!agent.Motion.IsImmobilized) {
                    if (!CheckRepairStation(agent, ticks)) {
                        if (hasGoal && AIGoal.IsActive(g.State)) {
                            g.ChangeStateTo(AIGoalState.Failed, ticks);
                        }
                        return;
                    }
                    if (!hasGoal) {
                        g = CreateMaintenanceGoal(ticks, agent);
                    }
                    g.CheckDoable = AIActionUseRepairStation.CheckDoable;
                    if (g.State != AIGoalState.Available) {
                        g.ChangeStateTo(AIGoalState.Available, ticks);
                        g.CreatedAt = ticks;
                        g.ResetWorldState();
                    }
                    if (agent.S.Sys.Combat.IsHostileCombatInProgress) {
                        // in combat, self-repair is urgent — interrupt immediately
                        g.WithInterrupt(2, 0);
                        if (agent.CombatUnit.Config == Combat.CombatUnitConfig.Fight) {
                            g.GoalImportance = 0;
                            g.GoalPriority = 1;
                        } else if (agent.CombatUnit.Config == Combat.CombatUnitConfig.Flee ||
                                data.Body.InjuryPercent > 0.3f) {
                            g.GoalImportance = 1;
                            g.GoalPriority = 3;
                        } else {
                            // free will and low injuries
                            g.GoalImportance = 1;
                            g.GoalPriority = 1;
                        }
                    } else {
                        // peacetime — let short tasks finish before seeking repairs
                        g.WithInterrupt(2, Consts.TicksPerHour);
                        g.GoalImportance = 1;
                        g.GoalPriority = 3;
                    }
                } else {
                    if (ticks % Consts.TicksPer2Hours == 14 && agent.Being.Goal == null
                            && data.IsFriendly) {
                        if (!data.HasStateFlag(BeingState.InDevice)
                                && !agent.Being.Conditions.TryGetGoal(out _)) {
                            var colonyG = AIGoal.Create(JobTypeIdH.Hauling,
                                T.RescueBeing_V1.F(data.Name), agent.Being.Conditions, ticks)
                                .WithIcon(IconId.CWrench2)
                                .WithWorldToDesired(AIVarsH.IsCarriedToHealingDevice, false, true)
                                .WithRequiredAbilities(AbilityIdH.Carry)
                                .WithPriority(4)
                                .WriteInitialState();
                            agent.Being.TrySetGoal(colonyG);
                        }
                    }
                }
            } else {
                if (hasGoal) {
                    if (g.State == AIGoalState.Available) {
                        g.ChangeStateTo(AIGoalState.Completed, ticks);
                    }
                }
            }
        }

        // Downed bots included: they are the ones that need a station most, and a player who
        // has never built one does not know what repairs them. No latch here - this need is
        // shared by every game in the session, so a flag on it suggested once per app run.
        // SuggestionSys keeps the per-game repeat timer.
        private static void MaybeSuggestRepairStation(GameState s, BeingData data, long ticks) {
            if (ticks % Consts.TicksPerHour != 0 || !data.IsFriendly) {
                return;
            }
            if (!s.Sys.Slots.HasFor(SlotDesignationH.BotRepair)) {
                s.Sig.AddSuggestion.Send(SuggestionBuildRepairStation.SuggId);
            }
        }

        private bool CheckRepairStation(AIAgentComp agent, long ticks) {
            if (agent.S.Sys.Slots.HasFor(SlotDesignationH.BotRepair)) {
                return true;
            }
            agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
            return false;
        }

        private AIGoal CreateMaintenanceGoal(long ticks, AIAgentComp agent) {
            AIGoal g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                IconId.CWrench2, T.JobTypeMaintenance, agent, ticks)
                .WithWorldToDesired(AIVarsH.IsBotDamaged, true, false)
                .WriteInitialState();
            agent.BB.SetNeedGoal(IdH, g);
            return g;
        }
        public bool ForceStartMaintenance(AIGoal goal, Being patient, Slot repSlot, long ticks) {
            var agent = patient.AIAgent;
            bool hasGoal = agent.BB.TryNeedGoal(IdH, out var g);
            if (!hasGoal) {
                g = CreateMaintenanceGoal(ticks, agent);
            }
            // not ideal to do it this way, but we can't use natural planning
            // or the patient may want to do something else rather than use healing device
            var plan = new AIPlan();
            plan.Steps = new AIAction[] {
                Registries.AIActions.FastGet(AIActionUseRepairStation.ActIdH)
            };
            agent.SetGoal(g, plan, ticks);
            if (agent.BB.ReservedSlot != repSlot) {
                if (agent.BB.ReservedSlot != null) {
                    D.Err("Unexpected, repair station slot not reserved! exp: {0} => actual: {1} (res goal: {2})",
                        repSlot, agent.BB.ReservedSlot, agent.BB.ReservedSlotGoal);
                }
                return agent.BB.TryReserveSlot(repSlot, goal);
            }
            return true;
        }

        public override void InitializeFor(BeingData data) {
        }
        public override void InitializeForNew(BeingData data) {
        }

        public AINeedMaintenance() : base(NeedId.Maintenance) {}
    }
}
