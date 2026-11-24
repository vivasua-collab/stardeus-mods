using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AINeedElectricity : AINeed {
        [SelfInit]
        private static void Register() {
            RegisterNeed(new AINeedElectricity());
        }
        public override int[] RelatedStats => new int[] {
            StatIdH.BatteryCharge,
            StatIdH.BatteryCapacity,
            StatIdH.BatteryConsumption,
        };

        private Curve priorityCurve = new float[] {
            0.0f, 5f,
            0.15f, 4f,
            0.3f, 3f,
            1.0f, 1f,
        };

        public override bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            var stats = data.StatsBeing;
            var cap = stats.BatteryCapacity.Value;
            iconId = IconId.CEmergencyRecharge;
            priority = 1f;
            // Charge is in battery units, not 0-1 like the other needs, so compare the
            // fraction. No battery at all counts as empty, same as before.
            return cap < 0.001f || stats.BatteryCharge.Value / cap < 0.2f;
        }

        public override bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            var stats = data.StatsBeing;
            var cap = stats.BatteryCapacity.Value;
            satisfaction = cap < 0.001f ? 0f
                : Maths.Clamp(stats.BatteryCharge.Value / cap, 0f, 1f);
            return true;
        }

        public override void Tick(GameState s, BeingData data, long ticks) {
            var stats = data.StatsBeing;
            var cap = stats.BatteryCapacity.Value;
            var curCharge = stats.BatteryCharge.Value;
            float newCharge;
            if (data.Body.IsIncapacitated) {
                // don't drop charge while incapacitated
                newCharge = curCharge;
            } else {
                var dropRate = stats.BatteryConsumption.Value;
                if (cap < 0.001f) {
                    newCharge = 0f;
                } else {
                    newCharge = Maths.Clamp(curCharge - dropRate, 0f, cap);
                }
                stats.BatteryCharge.UpdateBase(newCharge);
            }
            if (data.Being.TryGetComponent<NeedsComp>(out var needs)) {
                if (cap > 0f) {
                    var pct = newCharge / cap;
                    if (pct < 0.5f) {
                        // show battery level
                        var bar = needs.ShowInfoBar(Consts.ColorBatteryBar,
                        newCharge / cap, newCharge, stats.BatteryCapacity.ValueInt,
                            T.PercentageChargeBar_V1.F(
                                Units.Percentage(pct)));
                        bar.IsPretranslated = true;
                        needs.ShowingNeedInfo.Add(IdH);
                    } else {
                        HideInfoBar(needs);
                    }
                } else {
                    HideInfoBar(needs);
                }
            }
            if (cap > 0f) {
                if (newCharge < 0.0001f && !data.HasCondition(ConditionIdH.Discharged)) {
                    if (!ConditionState.CreateFor(s, ConditionIdH.Discharged, data, Severity.Irrelevant, false)) {
                        D.Err("Failed creating discharged condition on being! {0}", data);
                    }
                }
                // keep the recharge goal alive even at zero charge, so it's already
                // Available the instant the being recovers (e.g. via emergency
                // recharge or being docked) instead of re-draining before it's
                // re-armed
                CheckSatisfyGoal(data.Being.AIAgent, ticks, newCharge / cap);
            }
        }

        private void HideInfoBar(NeedsComp needs) {
            if (needs.ShowingNeedInfo.Remove(IdH)) {
                needs.HideInfoBar();
            }
        }

        private void CheckSatisfyGoal(AIAgentComp agent, long ticks, float needValue) {
            if (!agent.BB.TryNeedGoal(IdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CEnergy,
                    T.Recharge, agent, ticks)
                    .WithWorldToDesired(AIVarsH.IsRecharged, false, true);
                agent.BB.SetNeedGoal(IdH, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (needValue > 0.75f) {
                // we'll never charge when over 75%
                if (g.State != AIGoalState.Completed) {
                    g.ChangeStateTo(AIGoalState.Completed, ticks);
                    g.ResetWorldState();
                }
                return;
            }

            SetAvailableWithCooldown(g, agent, ticks, Consts.TicksPer10Minutes,
                Consts.TicksPer15Minutes);
            g.GoalImportance = 0;
            // we want to map 0-1 to 0-5
            g.GoalPriority = (byte) (priorityCurve.Evaluate(needValue) + 0.5f);
            if (needValue < 0.1f) {
                // battery nearly dead — interrupt soon
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer15Minutes);
            } else if (needValue < 0.2f) {
                // low battery — 1 hour grace
                g.GoalImportance = 2;
                g.WithInterrupt(2, Consts.TicksPer4Hours);
            } else if (needValue < 0.3f) {
                // below comfortable — 3 hour grace
                g.GoalImportance = 1;
                g.WithInterrupt(1, Consts.TicksPer8Hours);
            } else {
                g.GoalImportance = 0;
                g.WithInterrupt(1, Consts.TicksPer12Hours);
            }
        }

        public override void InitializeForNew(BeingData data) {
            var stats = data.StatsBeing;
            stats.BatteryCharge.UpdateBase(stats.BatteryCapacity.ValueFresh);
        }

        public override void InitializeFor(BeingData data) {
        }

        public AINeedElectricity() : base(NeedId.Electricity) {}
    }
}
