using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionOnFire : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionOnFire());
        }
        public override Priority Importance => Priority.First;
        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
            cond.Owner.Inventory?.LooksChanged();
            cond.Owner.Inventory?.RebuildLooksNow();
            DoPanic(cond.Owner.Being);
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
            if (cond.Owner.Being != null) {
                Ready.When(WhenH.FirexLoaded, () => ValidateOnFire(cond));
            }
        }

        private void ValidateOnFire(ConditionState cond) {
            if (!cond.Owner.Being.Flammable.IsOnFire) {
                D.Err("Being has OnFire condition but is not burning! Removing condition: {0}",
                    cond.Owner);
                cond.Owner.Being.S.ActQ.Enqueue(() => {
                    cond.Owner.RemoveCondition(cond.Owner.Being.S, cond.Condition.IdH);
                });
            }
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            DealFireDamageTo(cond.Owner, cond.TicksInCondition(ticks), ticks);
        }
        private static void DoPanic(Being being) {
            if (being == null) { return; }
            if (!being.Data.Species.IsBiological
                    || !being.IsAlive
                    || being.Data.Mind.Health == null) {
                return;
            }
            if (being.Data.StatsBeing.Sanity.Value <= 0.03f) {
                return;
            }
            // The burns bring their own pain. At -50 a victim of an arsonist's breakdown nearly
            // always broke down next, often setting someone else on fire
            being.Data.Mind.Health.AddEffect(being.S, MentalEffect.CreateWithIcon(
                being.PosIdx, IconId.CFire,
                G.Ticks, Consts.TicksPerDay, T.OnFire, -20));
            // panic
            var g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CStress,
                T.ActPanic, being.AIAgent, G.Ticks)
                    .WithWorldToDesired(AIVarsH.MentalPanicked, false, true)
                .ForceAssigned()
                .NonInterruptable()
                .WithoutRetry();
            g.SetAgent(being.AIAgent);
            being.S.Query.ForceGoal.Ask(g, false);
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            cond.Owner.StatsBeing.MoveSpeed.RemoveModifier(FireSlow, false);
            cond.Owner.Being.Motion.Immobilize(ImmobilizeReasonIdH.OnFire, false);
            cond.Owner.Inventory?.LooksChanged();
            cond.Owner.Inventory?.RebuildLooksNow();
        }

        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier fireSlow;
        private static StatModifier FireSlow => fireSlow ??= StatModifier.AddPercent(-0.8f, T.CondOnfire);

        public static bool DealFireDamageTo(BeingData beingData,
                float exposureTicks, long ticks) {
            if (beingData == null || beingData.Being == null) {
                return false;
            }
            if (beingData.Species.IsBiological) {
                if (exposureTicks > Consts.TicksPer30Minutes) {
                    beingData.StatsBeing.MoveSpeed.TryAddModifier(FireSlow);
                }
                if (exposureTicks > Consts.TicksPerHour) {
                    beingData.Being.Motion.Immobilize(ImmobilizeReasonIdH.OnFire, true);
                }
            }
            bool damaged = false;
            var source = NamedEntity.For(T.Fire);
            foreach (var slot in beingData.Body.Slots.OrderBy(v => Rng.UValue)) {
                if (slot.IsPartRemoved) { continue; }
                if (slot.IsInternal) { continue; }
                var expSev = SeverityFromExposure(
                    exposureTicks * Rng.URange(0.75f, 1.25f),
                    beingData,
                    out var burnChance);
                if (Rng.UChance(burnChance * slot.Size)) {
                    if (slot.TryAddOrUpdateWound(source,
                        DamageType.Heat, expSev, ticks, 1f,
                            conditionId: ConditionIdH.OnFire)) {
                        damaged = true;
                    }
                    if (Rng.UChance(0.5f)) { break; }
                }
            }
            return damaged;
        }

        private static Severity SeverityFromExposure(float ticks, BeingData being,
                out float burnChance) {
            // bigger is less damage!!!!!
            var multiplier = 1f;
            var isBiological = being.Species.IsBiological;
            if (isBiological) {
                var ssInteg = being.StatsBeing.SpaceSuitIntegrity.Value;
                if (ssInteg > 0.99f) { // full suit on!
                    multiplier *= 3f;
                } else if (ssInteg > 0.5f) { // no helmet but suit only
                    multiplier *= 1.5f;
                }
            } else {
                // non biological
                multiplier = 5f;
            }

            if (ticks < Consts.TicksPer30Minutes * multiplier) {
                burnChance = 0.03f;
                return Severity.Mild;
            } else if (ticks < Consts.TicksPerHour * multiplier) {
                burnChance = 0.04f;
                return Severity.Moderate;
            } else if (ticks < Consts.TicksPer2Hours * multiplier) {
                burnChance = 0.06f;
                return Severity.Severe;
            } else if (ticks < Consts.TicksPer3Hours * multiplier) {
                burnChance = 0.1f;
                return Severity.Critical;
            } else {
                burnChance = 0.15f;
                if (isBiological) {
                    return Severity.Fatal;
                } else {
                    return Severity.Irreparable;
                }
            }
        }

        private ConditionOnFire() : base(ConditionId.OnFire, IconId.CFire) {
            PreventsColonyWork = true;
            PreventsControl = true;
        }
    }
}
