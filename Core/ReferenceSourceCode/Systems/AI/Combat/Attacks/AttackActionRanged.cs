using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Combat;

namespace Game.Systems.AI.Combat {
    public sealed class AttackActionRanged : AttackAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AttackActionRanged());
        }
        public const string ActId = "Ranged";
        private AttackActionRanged() : base(ActId, () => T.CombatStateRangedattack) { }

        public override void OnCharge(GameState s, AttackMethod method,
                CombatUnit unit, IAttackable target) {
            var p = target.AttackablePosition;
            // D.Warn("Method.chargeTicks: {0}", method.ChargeTicks);
            if (method.ChargeTicks == 0) {
                // if (s.Sys.Path.IsCrowdedAt(unit.PosIdx, 1)) {
                //     if (method.IsMelee) {
                //         // FIXME this can be the reason why ranged units are dancing instead of
                //         // moving away to a better position
                //         // unit.TryShuffle(s, target);
                //     } else {
                //         D.Warn("Interrupting: {0}", unit.Agent.Goal);
                //         AIGoal.Interrupt(unit.Agent.Goal, G.Ticks);
                //     }
                // } else {
                    unit.ShowFX(s, method.ChargeVFX, p, true, method.EffectiveFullChargeTicks);
                // }
            }
            FacePosition(unit, p);
        }

        // Shown once the shot is resolved, not on the last charge tick as it used to be: a
        // miss flies past the target to wherever it ended (StrayShot)
        public override void OnShot(GameState s, AttackMethod method, CombatUnit unit,
                Vector2 impact) {
            unit.ShowFX(s, method.AttackVFX, impact, false,
                method.EffectiveCooldownTicks + method.EffectiveFullChargeTicks,
                Vectors.Distance(unit.Position, impact));
            if (Light.LightFlashes.TryShotColor(method.Type, out var flash)) {
                s.Sys.Light?.Flashes?.AddShot(unit.NozzlePosition, flash);
            }
            Sounds.PlayRandomAt(unit.Position, method.AttackSounds, true);
        }
    }
}
