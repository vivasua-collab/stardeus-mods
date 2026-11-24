using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Sleep;
using Game.Systems.Stats;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedHealth : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedHealth());
        }

        public override int[] RelatedStats => new int[] {
            StatIdH.BloodAmount,
            StatIdH.BloodCapacity,
            StatIdH.BloodClotRate,
            StatIdH.BloodFilterRate,
            StatIdH.BloodLossRate,
            StatIdH.BloodRegenRate,
            StatIdH.BloodToxicity,
            StatIdH.ImmunityGainRate,
            StatIdH.ImmunityLossRate,
            StatIdH.SurgicalFatigue,
        };

        private Curve immunityGainByAge;
        private Curve immunityLossByAge;
        private Curve surgeryFatigueRecoveryByAge;

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = IconId.CHealth;
            priority = data.StatsBeing.BloodLossRate.Value > 0f ? 4.5f : 4f;
            return data.Body.CanHeal || data.Body.IsIncapacitated;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = BodyIntegrity(data);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            ProcessBlood(data);
            BloodFiltration.Tick(s, data, ticks);
            ProcessHealing(s, data, ticks);
            ProcessSurgicalFatigue(data);
            if (ticks % Consts.TicksPerWeek == 0) {
                UpdateImmunity(data);
            }
        }

        public bool ForceStartHealing(Being patient, BedSlot bedSlot, long ticks) {
            var agent = patient.AIAgent;
            bool hasGoal = agent.BB.TryNeedGoal(IdH, out var g);
            if (!hasGoal) {
                g = CreateHealingGoal(ticks, agent);
            }
            // not ideal to do it this way, but we can't use natural planning
            // or the patient may want to do something else rather than use healing device
            var plan = new AIPlan();
            plan.Steps = new AIAction[] {
                Registries.AIActions.FastGet(AIActionSleepInBed.ActIdH),
                Registries.AIActions.FastGet(AIActionHealInBed.ActIdH),
            };
            agent.SetGoal(g, plan, ticks);
            agent.BB.Object1 = bedSlot.Comp;
            agent.BB.Object2 = bedSlot;
            return true;
        }
        public bool ForceStartHealing(AIGoal goal, Being patient, Slot healerSlot, long ticks) {
            var agent = patient.AIAgent;
            bool hasGoal = agent.BB.TryNeedGoal(IdH, out var g);
            if (!hasGoal) {
                g = CreateHealingGoal(ticks, agent);
            }
            // not ideal to do it this way, but we can't use natural planning
            // or the patient may want to do something else rather than use healing device
            var plan = new AIPlan();
            plan.Steps = new AIAction[] {
                Registries.AIActions.FastGet(AIActionUseHealingDevice.ActIdH)
            };
            if (agent.Goal != null) {
                if (!AIGoal.Interrupt(agent.Goal, ticks)) {
                    D.Warn("Tried to force start healing for someone who had a non-interruptable goal! {0} | {1}",
                        agent.Goal, agent);
                    return false;
                }
            }
            g.ResetWorldState();
            agent.SetGoal(g, plan, ticks);
            if (agent.BB.ReservedSlot != healerSlot) {
                if (agent.BB.ReservedSlot != null) {
                    D.Err("Unexpected, healing slot not reserved! exp: {0} => actual: {1} (res goal: {2})",
                        healerSlot, agent.BB.ReservedSlot, agent.BB.ReservedSlotGoal);
                }
                return agent.BB.TryReserveSlot(healerSlot, goal);
            }
            return true;
        }

        private void ProcessHealing(GameState s, BeingData data, long ticks) {
            if (!data.Body.IsAlive && !data.EffectiveFaction.IsPlayer) {
                return;
            }
            var agent = data.Being.AIAgent;
            bool hasGoal = agent.BB.TryNeedGoal(IdH, out var g);
            if (data.HasCondition(ConditionIdH.OnFire) || agent.Goal?.IsNonInterruptable == true) {
                // don't heal while on fire!
                if (hasGoal) {
                    AIGoal.Cancel(g, ticks);
                }
                return;
            }
            if (hasGoal && g.State == AIGoalState.InProgress) {
                // a raider on its way to our bed or AutoDoc that starts fleeing or leaving keeps
                // running (HostileSelfCare)
                if (HostileSelfCare.ShouldDropTrip(data)) {
                    AIGoal.Cancel(g, ticks);
                }
                // never touch goal that is in progress
                return;
            }
            if (!data.IsFriendly && !HostileSelfCare.MayStart(data)) {
                // the only ways to heal are the crew's beds and medical devices: only a raider
                // on the attack with nobody around comes to use them (STA-1816)
                if (hasGoal && AIGoal.IsActive(g.State)) {
                    g.ChangeStateTo(AIGoalState.Failed, ticks);
                }
                return;
            }
            var isIncapacitated = data.Body.IsIncapacitated;
            if (isIncapacitated && agent.CurrentAction != null) {
                if (agent.CurrentAction.IdH != AIActionSleepInBed.ActIdH) {
                    D.Warn("Incapacitated with action: {0}: {1}",
                        agent, agent.CurrentAction);
                }
            }
            if (data.Body.CanHeal || isIncapacitated) {
                if (!hasGoal) {
                    g = CreateHealingGoal(ticks, agent);
                }
                SetAvailableWithCooldown(g, agent, ticks, Consts.TicksPer15Minutes,
                    Consts.TicksPer30Minutes);
                // less important than combat
                if (agent.BeingData.StatsBeing.BloodLossRate.Value > 0f) {
                    g.GoalImportance = 2;
                    if (agent.BeingData.IsFriendly) {
                        // actively bleeding friendly being - interrupt immediately
                        g.WithInterrupt(3, 0);
                        g.GoalPriority = 5;
                    } else {
                        g.GoalPriority = 2;
                    }
                } else {
                    // injured but stable — 4 hour grace before interrupting
                    g.GoalImportance = 1;
                    g.GoalPriority = 4;
                    g.WithInterrupt(2, Consts.TicksPer4Hours);
                }
                if (agent.Motion.IsImmobilized && CanRequestRescue(data, agent, ticks)) {
                    // Downed somewhere that is killing them: worth a rescue even with
                    // nowhere to heal, AIActionCarryToHealingDevice then carries them
                    // to the nearest safe tile (STA-1393)
                    TryRequestRescue(data, agent, ticks,
                        AIActionCarryToHealingDevice.IsHealingDeviceAvailableFor(s, data));
                }
            } else {
                if (hasGoal) {
                    if (g.State == AIGoalState.Available) {
                        g.ChangeStateTo(AIGoalState.Completed, ticks);
                    }
                }
                // Nothing to heal, but can't walk to a bed or food either (23XKG). The
                // in-bed check stops a rescue from pulling them out of a bed they're in.
                if (data.IsBedridden && CanRequestRescue(data, agent, ticks)
                        && agent.CurrentAction?.IdH != AIActionSleepInBed.ActIdH) {
                    TryRequestRescue(data, agent, ticks,
                        s.Query.FindBed.Ask(data, true) != null);
                }
            }
        }

        private bool CanRequestRescue(BeingData data, AIAgentComp agent, long ticks) {
            return agent.Being.Goal == null && data.IsFriendly
                && !data.HasStateFlag(BeingState.InDevice)
                && !agent.BB.IsCoolingDown(IdH, ticks);
        }

        private void TryRequestRescue(BeingData data, AIAgentComp agent, long ticks,
                bool hasPlaceToHeal) {
            if (hasPlaceToHeal || !data.Safety.IsActuallySafeAt(agent.Being.PosIdx)) {
                if (!agent.Being.Conditions.TryGetGoal(out var colonyG)) {
                    colonyG = AIGoal.Create(JobTypeIdH.Hauling,
                        T.RescueBeing_V1.F(data.Name), agent.Being.Conditions, ticks)
                        .WithIcon(IconId.CHealth)
                        .WithWorldToDesired(AIVarsH.IsCarriedToHealingDevice, false, true)
                        .WithRequiredAbilities(AbilityIdH.Carry)
                        .WithPriority(4)
                        .WriteInitialState();
                    agent.Being.TrySetGoal(colonyG);
                }
            } else {
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer5Minutes, Consts.TicksPer10Minutes);
            }
        }

        private AIGoal CreateHealingGoal(long ticks, AIAgentComp agent) {
            AIGoal g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                IconId.CHealth, T.HealingInjuries, agent, ticks)
                .WithWorldToDesired(AIVarsH.IsInjured, true, false)
                .WriteInitialState();
            agent.BB.SetNeedGoal(IdH, g);
            return g;
        }

        private void ProcessSurgicalFatigue(BeingData data) {
            var stats = data.StatsBeing;
            var fatigue = stats.SurgicalFatigue;
            if (fatigue.BaseValue > 0f) {
                var agePercent = data.Body.AgePercent;
                var pctPerDay = surgeryFatigueRecoveryByAge.Evaluate(agePercent);
                var pctPerTick = pctPerDay / Consts.TicksPerDay;
                fatigue.TryDrain(pctPerTick);
            }
        }

        private static void ProcessBlood(BeingData data) {
            var stats = data.StatsBeing;
            var bloodAmt = stats.BloodAmount;
            var bloodCapVal = stats.BloodCapacity.Value;
            var bloodAmtVal = bloodAmt.Value;
            var needsConditionCheck = false;
            bool needShowBloodBar = false;
            if (bloodAmtVal < bloodCapVal) {
                needShowBloodBar = true;
                // percent per hour
                var bloodRegenPctPerHour = stats.BloodRegenRate.Value;
                if (bloodRegenPctPerHour > 0f) {
                    // the being's own capacity, not its species average (they differ by a few %)
                    var regenVal = (bloodRegenPctPerHour * bloodCapVal) / Consts.TicksPerHour;
                    var newBloodAmt = bloodAmtVal + regenVal;
                    if (newBloodAmt >= bloodCapVal) {
                        newBloodAmt = bloodCapVal;
                        needShowBloodBar = false;
                        data.Being.Persona.HideInfoBar();
                        needsConditionCheck = true;
                    }
                    bloodAmt.UpdateBase(newBloodAmt);
                    bloodAmtVal = newBloodAmt;
                }
                if (needShowBloodBar) {
                    data.Being.Persona.ShowInfoBar(Consts.ColorHealthBar,
                        bloodAmtVal / bloodCapVal, bloodAmtVal * 1000f, (int)(bloodCapVal * 1000f),
                            "blood.amount.bar");
                }
            } else if (bloodAmt.Value > bloodCapVal) {
                D.Err("Blood capacity was reduced, reducing blood amount! {0} -> {1} ({2})",
                    bloodAmt, bloodCapVal, data);
                bloodAmt.UpdateBase(bloodCapVal);
            }
            if (needsConditionCheck) {
                data.Body.CheckCondition(false);
            }
        }

        public override void InitializeFor(BeingData data) {
            AssignCurves();
            UpdateImmunity(data);
        }
        public override void InitializeForNew(BeingData data) {
            AssignCurves();
            InitializeBlood(data);
            UpdateImmunity(data);
        }

        private void UpdateImmunity(BeingData data) {
            var gain = immunityGainByAge.Evaluate(data.Body.AgePercent);
            var loss = immunityLossByAge.Evaluate(data.Body.AgePercent);
            var fluc = new Rng(data.Id).Range(0.95f, 1.05f);
            data.StatsBeing.ImmunityGainRate.UpdateBase(gain * fluc);
            fluc = new Rng(data.Id).Range(0.95f, 1.05f);
            data.StatsBeing.ImmunityLossRate.UpdateBase(loss * fluc);
        }

        private void InitializeBlood(BeingData data) {
            // fluctuate the blood amount
            // could be M (masculine), F (feminine), L (large), T (thin), N (neutral)
            var bodyType = data.Looks.BodyType;
            char type;
            if (bodyType != 'N') {
                type = bodyType;
            } else if (data.Body.Sex != null) {
                type = data.Body.Sex.TypeChar;
            } else {
                type = 'N';
            }
            float fluc;
            switch (type) {
                case 'M': fluc = 1.04f; break;
                case 'F': fluc = 0.97f; break;
                case 'T': fluc = 0.95f; break;
                case 'L': fluc = 1.05f; break;
                default: fluc = 1f; break;
            }
            fluc *= Rng.URange(0.99f, 1.01f);
            data.StatsBeing.BloodRegenRate.UpdateBase(0.03f);
            data.StatsBeing.BloodCapacity.UpdateBase(
                data.Species.BloodCapacityLiters * fluc);
            data.StatsBeing.BloodAmount.UpdateBase(
                data.StatsBeing.BloodCapacity.BaseValue);
        }

        private void AssignCurves() {
            if (immunityGainByAge == null) {
                immunityGainByAge = Tunables.ImmunityGainByAgeCurve;
                immunityLossByAge = Tunables.ImmunityLossByAgeCurve;
                surgeryFatigueRecoveryByAge = CurveDef.FastGet(CurveIdH.Surgery_SurgicalFatigueRecoveryRate_Age);
            }
        }

        public AINeedHealth() : base(NeedId.Health) { }
    }
}
