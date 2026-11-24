using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    public sealed class ConditionCryoSickness : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionCryoSickness());
        }

        public override Priority Importance => Priority.Normal;

        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
            // Mild → ~6h, Moderate → ~12h, Severe → ~18h, ±1h randomness
            var baseTicks = cond.Severity switch {
                Severity.Mild => Consts.TicksPer8Hours,
                Severity.Moderate => Consts.TicksPer12Hours,
                _ => Consts.TicksPer18Hours,
            };
            cond.Duration = baseTicks + Rng.URange(-Consts.TicksPerHour, Consts.TicksPerHour);
        }

        // OnLoad intentionally empty — Duration is persisted, do not reset remaining time.

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            var data = cond.Owner;
            // Suppress while hidden (inside device / shuttle / expedition) or dead
            if (data.SkipSimulate) { return; }
            // Suppress while asleep
            if (data.HasCondition(ConditionIdH.Sleeping)) { return; }
            // Vomit chance scales with severity: Mild→1/h, Moderate→2/h, Severe→3/h
            float vomitChancePerTick = (float)cond.Severity / Consts.TicksPerHour;
            if (Rng.UChance(vomitChancePerTick)) {
                var being = data.Being;
                if (being == null) { return; }
                Vector2 pos = being.Position;
                s.Sys.Dirt.Add(DirtData.Create(pos,
                    Images.RandomMatching(GraphicsId.DirtPuke01, 2),
                    Rng.URange(0.75f, 1.25f)));
            }
        }

        private ConditionCryoSickness() : base(ConditionId.CryoSickness, IconId.CDazed) {
            PreventsColonyWork = true;
        }
    }
}
