using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;

namespace Game.Systems.Conditions {
    // Pain shock: a being whose pain passes its pain threshold goes down, and gets up
    // once the pain drops back. Most fights end in downs this way, long before a vital part
    // gives out or the blood runs out, and the downed can be rescued, captured or finished.
    // Robots have the same mechanic as system shock (their Pain stat is hidden).
    // One check for both, run by the Injured and Damaged conditions after they total the wounds'
    // pain each tick, so it reacts within a tick of a hit and never walks the body twice.
    public static class PainShock {
        private const float PainEpsilon = 0.0001f;

        public static void Update(GameState s, BeingData owner, float woundPain) {
            var stats = owner.StatsBeing;
            stats.WoundPain.SetTotal(woundPain, stats.Pain);
            var shockIdH = ShockConditionFor(owner);
            var pain = stats.Pain.ValueFresh;
            var threshold = stats.PainThreshold.ValueFresh;
            if (!owner.HasCondition(shockIdH)) {
                // at the threshold, not only past it: two Critical wounds (0.4 each) down a
                // human at exactly 0.8, as WoundSeverity_Pain intends. The epsilon keeps a sum
                // that lands a float step short of it from missing
                if (pain >= threshold - PainEpsilon) {
                    ConditionState.CreateFor(s, shockIdH, owner, Severity.Critical, true);
                }
            } else if (pain < threshold - Tunables.PainShockRecoverMargin) {
                // a margin, or a pain right at the threshold would flip the being up and down
                owner.RemoveCondition(s, shockIdH);
            }
        }

        // Totals the wounds' pain itself and updates the shock now, for callers that cannot
        // wait for the next condition tick (the durability sweep deals its hits with the
        // clock stopped)
        public static void Recheck(GameState s, BeingData owner) {
            var isBio = owner.Species.IsBiological;
            float pain = 0f;
            foreach (var slot in owner.Body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                var wounds = slot.State?.Wounds;
                if (wounds == null) { continue; }
                for (int i = wounds.Count - 1; i >= 0; i--) {
                    var w = wounds[i];
                    // the same rules as ConditionInjured (flesh) and ConditionDamaged (robots)
                    if (isBio) {
                        if (w.IsSettled) { continue; }
                        pain += WoundPainSource.PainOf(WoundPain.EffectiveSeverity(w, slot));
                    } else if (w.Severity < Severity.Irreparable) {
                        pain += WoundPainSource.PainOf(w.Severity);
                    }
                }
            }
            Update(s, owner, pain);
        }

        public static int ShockConditionFor(BeingData being) {
            return being.Species.IsBiological ? ConditionIdH.PainShock : ConditionIdH.SystemShock;
        }

        // A shock outlives nothing that hurts: once the wounds are gone, so is the condition
        // that totals them, and the shock ends with it
        public static bool HasWoundCondition(BeingData being) {
            return being.HasCondition(ConditionIdH.Injured)
                || being.HasCondition(ConditionIdH.Damaged);
        }
    }
}
