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
    public sealed class AttackActionMelee : AttackAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AttackActionMelee());
        }
        public const string ActId = "Melee";
        private AttackActionMelee() : base(ActId, () => T.CombatStateMeleeattack) { }

        public override void OnCharge(GameState s, AttackMethod method,
                CombatUnit unit, IAttackable target) {
            var p = target.AttackablePosition;
            if (method.ChargeTicks == 0) {
                // if (s.Sys.Path.IsCrowdedAt(unit.PosIdx, 1)) {
                //     unit.TryShuffle(s, target);
                // }
            } else if (method.ChargeTicks >= method.EffectiveFullChargeTicks - 1) {
                if (unit.Agent.Being.Graphics.PunchAtWithCallback(
                        p - unit.Position,
                        0.7f, 0.2f, 0.3f, () => {
                            // the swing; the target's hurt sounds wait for a hit (OnExecute)
                            Sounds.PlayRandomAt(p, method.AttackSounds, true);

                            // Can't recoil because it prevents the target from
                            // performing attack animation and it looks like
                            // the target is getting beaten up without resistance
                            // // recoil enemy
                            // target.Agent.Worker.Graphics.Punch(target.Position - unit.Position,
                            //     0.1f, 0.1f, 0.15f);
                        })) {
                    if (Rng.UChance(0.3f)) {
                        // we started the attack
                        Sounds.PlayRandomAt(unit.Position, unit.Data.Species.AttackSounds, true);
                    }
                }
            }
            FacePosition(unit, p);
        }

        public override bool OnExecute(GameState s, AttackMethod method, CombatUnit unit,
                IAttackable target, bool hit, float chance) {
            if (hit) {
                // These played with the swing, so a miss sounded like a hit
                var p = target.AttackablePosition;
                if (target is CombatUnit cu && Rng.UChance(0.3f)) {
                    Sounds.PlayRandomAt(p, cu.Data.Species.PainSounds, true);
                }
                Sounds.PlayRandomAt(p, target.TakeDamageSounds, true);
            } else if (target is CombatUnit dodger && !dodger.Data.Body.IsIncapacitated) {
                // A hit flashes red, so a miss needs a look of its own: the target leans out of
                // the blow's way, to a random side (STA-1793)
                dodger.Agent.Being?.Looks?.Dodge.Dodge(GameTimer.time,
                    dodger.Position - unit.Position, Rng.UChance(0.5f));
            }
            return hit;
        }
    }
}
