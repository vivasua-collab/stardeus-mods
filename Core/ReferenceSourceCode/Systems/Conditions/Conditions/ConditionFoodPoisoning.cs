using System;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    public sealed class ConditionFoodPoisoning : ConditionWithImmunity {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionFoodPoisoning());
        }
        public override Priority Importance => Priority.High;
        public override int MoodSourceH => MoodSourceIdH.FoodPoisoning;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier mod1;
        private static StatModifier Mod1 => mod1 ??= StatModifier.AddPercent(3f, T.BreakdownBreakdownfoodpoisoning);
        private static StatModifier mod2;
        private static StatModifier Mod2 => mod2 ??= StatModifier.AddPercent(5f, T.BreakdownBreakdownfoodpoisoning);
        private static StatModifier mod3;
        private static StatModifier Mod3 => mod3 ??= StatModifier.AddPercent(10f, T.BreakdownBreakdownfoodpoisoning);
        private static StatModifier mod4;
        private static StatModifier Mod4 => mod4 ??= StatModifier.AddPercent(15f, T.BreakdownBreakdownfoodpoisoning);

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
            EnsureModifier(cond);
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            RemoveAnyModifiers(cond.Owner.StatsBeing);
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        private void EnsureModifier(ConditionState cond) {
            var stats = cond.Owner.StatsBeing;
            RemoveAnyModifiers(stats);
            switch (cond.Severity) {
                case Severity.Irrelevant: { return; }
                case Severity.Mild: {
                    stats.ToiletRate.AddModifier(Mod1, false);
                    break;
                }
                case Severity.Moderate: {
                    stats.ToiletRate.AddModifier(Mod2, false);
                    break;
                }
                case Severity.Severe: {
                    stats.ToiletRate.AddModifier(Mod3, false);
                    break;
                }
                case Severity.Critical: {
                    stats.ToiletRate.AddModifier(Mod4, false);
                    break;
                }
                case Severity.Fatal: {
                    stats.ToiletRate.AddModifier(Mod4, false);
                    break;
                }
            }
        }

        private static void RemoveAnyModifiers(StatsBeing stats) {
            stats.ToiletRate.RemoveModifier(Mod1, false);
            stats.ToiletRate.RemoveModifier(Mod2, false);
            stats.ToiletRate.RemoveModifier(Mod3, false);
            stats.ToiletRate.RemoveModifier(Mod4, false);
        }

        private void Vomit(GameState s, ConditionState cond, long ticks) {
            // It reads as soiling, which can't happen on a toilet. Once vomiting has its
            // own mood, it should apply on a toilet too
            if (AIActionUseToilet.IsSeated(cond.Owner)) { return; }
            cond.Owner.Mind.Health?.AddEffect(s,
                MentalEffect.Create(cond.Owner.Being?.PosIdx ?? -1, ticks,
                    Consts.TicksPer4Hours, T.SoiledThemselves, -20));
            cond.Owner.Body.AddExcrementsOnFloor();
        }

        protected override void OnTick(GameState s, ConditionState cond, long ticks) {
            var devRate = 1f / Consts.TicksPerDay;
            var mental = cond.Owner.Mind.Health;
            if (mental != null && ticks % Consts.TicksPerHour == 0) {
                SetMood(s, cond, IconId.CPoison, T.BreakdownBreakdownfoodpoisoning,
                    (int) cond.Severity * -2);
            }
            TryVomit(s, cond, ticks);
            if (ticks % Consts.TicksPer3Hours == 13) {
                // damage
                var sev = cond.Severity;
                if (sev > Severity.Mild) {
                    sev--;
                }
                BeingUtils.DamageRandomBodyPart(s, cond.Owner, Rng.Unseeded,
                    DamageType.Organ, sev,
                    NamedEntity.For(NameT),
                    skipInternal: false,
                    inclusionCheck: (slot) => OrganCheck(slot, cond.Severity),
                        conditionId: IdH);
            }
            if (Advance(s, ticks, IdH, cond.Owner, 1,
                    devRate, 0f, false, Severity.Critical)) {
                Vomit(s, cond, ticks);
                EnsureModifier(cond);
            }
            // TryShitPants(s, cond, ticks);
        }

        private void TryVomit(GameState s, ConditionState cond, long ticks) {
            float vomitChancePerTick = (float) cond.Severity / Consts.TicksPerHour;
            if (Rng.UChance(vomitChancePerTick)) {
                Vomit(s, cond, ticks);
            }
        }

        private bool OrganCheck(BodyPartSlot slot, Severity sev) {
            switch (slot.Type.IdH) {
                case BodyPartIdH.Stomach:
                    return true;
                case BodyPartIdH.Intestines:
                    return sev > Severity.Moderate;
            }
            return false;
        }

        private ConditionFoodPoisoning() : base(ConditionId.FoodPoisoning,
                ConditionId.FoodPoisoning_Immunity, 0f, IconId.CToilet) {
            nameTFunc = () => T.BreakdownBreakdownfoodpoisoning;
            descTFunc = () => T.BreakdownBreakdownfoodpoisoningDesc_V1.F(
                T.NounColonist);
            PreventsColonyWork = true;
        }
    }
}
