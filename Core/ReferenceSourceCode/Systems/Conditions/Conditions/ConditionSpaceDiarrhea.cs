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
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionSpaceDiarrhea : ConditionWithImmunity {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionSpaceDiarrhea());
        }
        public override Priority Importance => Priority.High;
        public override int MoodSourceH => MoodSourceIdH.SpaceDiarrhea;
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier mod1;
        private static StatModifier Mod1 => mod1 ??= StatModifier.AddPercent(5f, T.SpaceDiarrheaOngoing);
        private static StatModifier mod2;
        private static StatModifier Mod2 => mod2 ??= StatModifier.AddPercent(10f, T.SpaceDiarrheaOngoing);
        private static StatModifier mod3;
        private static StatModifier Mod3 => mod3 ??= StatModifier.AddPercent(20f, T.SpaceDiarrheaOngoing);
        private static StatModifier mod4;
        private static StatModifier Mod4 => mod4 ??= StatModifier.AddPercent(30f, T.SpaceDiarrheaOngoing);

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
            EnsureModifier(cond);
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            RemoveAnyModifiers(cond.Owner.StatsBeing);
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
        private void TryReduceToiletNeed(GameState s, ConditionState cond, long ticks) {
            if (AIActionUseToilet.IsSeated(cond.Owner)) { return; }
            float chanceTo = 0f;
            switch (cond.Severity) {
                case Severity.Mild: {
                    chanceTo = 0.05f;
                    break;
                }
                case Severity.Moderate: {
                    chanceTo = 0.1f;
                    break;
                }
                case Severity.Severe: {
                    chanceTo = 0.15f;
                    break;
                }
                case Severity.Critical: {
                    chanceTo = 0.20f;
                    break;
                }
                case Severity.Fatal: {
                    chanceTo = 0.25f;
                    break;
                }
            }
            if (Rng.UChance(chanceTo)) {
                if (!cond.Owner.StatsBeing.Toilet.TryDrain(0.05f)) {
                    cond.Owner.Body.AddExcrementsOnFloor();
                }
                if (cond.Owner.HasCondition(ConditionIdH.Sleeping)
                        && cond.Owner.StatsBeing.Toilet.Value < 0.15f) {
                    if (!cond.Owner.Body.IsIncapacitated) {
                        // wake up!
                        cond.Owner.Being?.AIAgent.DropGoal();
                    }
                }
            }
        }

        private void ShitPants(GameState s, ConditionState cond, long ticks) {
            if (AIActionUseToilet.IsSeated(cond.Owner)) { return; }
            cond.Owner.Mind.Health?.AddEffect(s,
                MentalEffect.Create(
                    cond.Owner.Being?.PosIdx ?? -1,
                    ticks, Consts.TicksPer4Hours, T.SoiledThemselves, -20));
            cond.Owner.Body.AddExcrementsOnFloor();
        }

        protected override void OnTick(GameState s, ConditionState cond, long ticks) {
            var devRate = 1f / Consts.TicksPerDay;
            var mental = cond.Owner.Mind.Health;
            if (mental != null && ticks % Consts.TicksPerHour == 0) {
                SetMood(s, cond, IconId.CToilet, T.SpaceDiarrheaOngoing,
                    (int) cond.Severity * -3);
            }
            if (ticks % Consts.TicksPer5Minutes == 0) {
                TryReduceToiletNeed(s, cond, ticks);
            }
            if (ticks % Consts.TicksPer3Hours == 13) {
                // damage
                BeingUtils.DamageRandomBodyPart(s, cond.Owner, Rng.Unseeded,
                    DamageType.Organ, cond.Severity, NamedEntity.For(NameT),
                    skipInternal: false,
                    inclusionCheck: (slot) => OrganCheck(slot, cond.Severity),
                        conditionId: ConditionIdH.SpaceDiarrhea);
            }
            if (Advance(s, ticks, ConditionIdH.SpaceDiarrhea, cond.Owner, 1,
                    devRate, 0f, false, Severity.Fatal)) {
                ShitPants(s, cond, ticks);
                EnsureModifier(cond);
            }
            // TryShitPants(s, cond, ticks);
        }

        private bool OrganCheck(BodyPartSlot slot, Severity sev) {
            switch (slot.Type.IdH) {
                case BodyPartIdH.Intestines:
                    return true;
                case BodyPartIdH.Kidney:
                case BodyPartIdH.Stomach:
                    return sev >= Severity.Moderate;
                case BodyPartIdH.Liver:
                    return sev >= Severity.Severe;
                case BodyPartIdH.Heart:
                    return sev >= Severity.Critical;
            }
            return false;
        }



        private ConditionSpaceDiarrhea() : base(ConditionId.SpaceDiarrhea,
                ConditionId.SpaceDiarrhea_Immunity, 1f, IconId.CToilet) {
            nameTFunc = () => T.EvtSpaceDiarrheaTitle;
            PreventsColonyWork = true;
        }
    }
}
