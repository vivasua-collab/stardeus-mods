using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionInspired : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionInspired());
        }
        public override Priority Importance => Priority.Low;
        public override int MoodSourceH => MoodSourceIdH.Inspired;
        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier inspiredSpeedMod;
        private static StatModifier InspiredSpeedMod => inspiredSpeedMod ??= StatModifier.AddPercent(1.25f, T.CondInspired);
        private static StatModifier inspiredNeedMod;
        private static StatModifier InspiredNeedMod => inspiredNeedMod ??= StatModifier.Sets(1f, T.CondInspired);
        public override void OnStart(ConditionState cond, long ticks) {
            cond.Owner.StatsBeing.MoveSpeed.AddModifier(InspiredSpeedMod);
            cond.Owner.StatsBeing.Toilet.AddModifier(InspiredNeedMod);
            cond.Owner.StatsBeing.Hygiene.AddModifier(InspiredNeedMod);
            cond.Owner.StatsBeing.Hunger.AddModifier(InspiredNeedMod);
            cond.Owner.StatsBeing.Sleep.AddModifier(InspiredNeedMod);
            cond.Owner.StatsBeing.Fun.AddModifier(InspiredNeedMod);
        }
        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            cond.Owner.StatsBeing.MoveSpeed.RemoveModifier(InspiredSpeedMod, false);
            cond.Owner.StatsBeing.Toilet.RemoveModifier(InspiredNeedMod, false);
            cond.Owner.StatsBeing.Hygiene.RemoveModifier(InspiredNeedMod, false);
            cond.Owner.StatsBeing.Hunger.RemoveModifier(InspiredNeedMod, false);
            cond.Owner.StatsBeing.Sleep.RemoveModifier(InspiredNeedMod, false);
            cond.Owner.StatsBeing.Fun.RemoveModifier(InspiredNeedMod, false);
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (!cond.Owner.Body.IsAlive) {
                cond.Owner.RemoveCondition(s, IdH);
                return;
            }
            SetMood(s, cond, IconId.CUpgrade, T.CondInspired, +10);
            var uninspireRate = 1f / (Tunables.InspirationSeverityDecreaseDurationHours * Consts.TicksPerHour);
            Advance(s, ticks, ConditionIdH.Inspired,
                    cond.Owner, -1,
                    0f,
                    uninspireRate,
                    false);
        }

        private ConditionInspired() : base(ConditionId.Inspired, IconId.CUpgrade) {
            IsPositive = true;
        }
    }
}
