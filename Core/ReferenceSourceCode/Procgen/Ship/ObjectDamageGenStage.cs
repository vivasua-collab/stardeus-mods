using KL.Grid;
using Game.Constants;
using Game.Components;
using KL.Utils;
using System.Collections;
using Game.Data;
using Game.CodeGen;
using UnityEngine;

namespace Game.Procgen {
    public class ObjectDamageGenStage : BaseShipGenStage {
        /// <summary>
        /// Destruction-level intensity, [0.2 .. 1.0]. 1.0 (default) reproduces
        /// the historic damage chance exactly; lower values damage fewer objects.
        /// </summary>
        private readonly float intensity;
        public ObjectDamageGenStage(float intensity = 1f) {
            this.intensity = intensity;
        }
        public override void Apply() {
            var r = rng.Fork();

            var found = S.Components.GenFindAllMatching<DamageableComp>((c) => {
                if (c.Entity.Definition.LayerId != WorldLayer.Objects) {
                    return false;
                }
                if (!IsWithinGenArea(c.Tile.PosIdx)) {
                    return false;
                }
                if (spec.HasStasisArray &&
                        c.Entity.Definition.IdH == DefIdsH.ObjectsDevicesStasisArray) {
                    return false;
                }
                if (spec.InvincibleParts?.Contains(c.Entity.PosIdx) == true) {
                    return false;
                }
                if (!spec.HasCPU) {
                    return true;
                }
                return Pos.SqrDistance(c.Entity.PosIdx, spec.CPUPos) > 12 * 12;
            });

            if (Arrays.IsEmpty(found)) {
                D.Warn("Could not find any damageable objects within gen area");
            } else {
                var baseSkip = Mathf.Clamp(1f - S.Difficulty.Level, 0.15f, 0.95f);
                var skipChance = Mathf.Lerp(1f, baseSkip, intensity);
                for (int i = found.Count - 1; i >= 0; i--) {
                    if (r.Chance(skipChance)) {
                        continue;
                    }
                    DamageableComp obj = found[i];
                    var mh = obj.Health;
                    obj.TakeDamage(r.Range(mh * 0.3f, mh * 0.7f),
                        r.From(damageTypes));
                }
            }
        }

        private static readonly DamageType[] damageTypes = new[] {
            DamageType.Explosive,
            DamageType.Heat,
            DamageType.Energy
        };
    }
}
