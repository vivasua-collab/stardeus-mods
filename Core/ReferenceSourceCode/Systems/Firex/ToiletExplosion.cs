using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Firex {
    /// <summary>
    /// Shared helper that produces the "toilet explosion" result used by both
    /// <see cref="Story.CleaningIncidentStoryEvent"/> and the
    /// SabotageToilet mental breakdown: acid damage to the toilet tile, a custom
    /// blast that sprays human-waste dirt in a radius, and an explosion sound.
    /// </summary>
    public static class ToiletExplosion {
        private static Curve defaultSizeDistCurve = new float[] {
            1f, 3.5f,
            1.5f, 2f,
            2f, 0.5f
        };
        /// <summary>
        /// Damages the toilet tile with acid, spawns human-waste dirt in a radius, and plays an
        /// explosion sound. Pass <paramref name="sizeDistanceCurve"/> = null to use the default
        /// curve (hard-coded mirror of CleaningIncident.json's SizeDistanceCurve).
        /// </summary>
        /// <param name="S">Game state.</param>
        /// <param name="toilet">The toilet tile to explode.</param>
        /// <param name="rng">RNG source.</param>
        /// <param name="fullness">Scales the blast radius (1 = max mess).</param>
        /// <param name="sizeDistanceCurve">
        /// Optional override for the dirt-size-vs-distance curve. Null → default.
        /// </param>
        public static void Explode(GameState S, Tile toilet, Rng rng, float fullness, Curve curve = null) {
            curve ??= defaultSizeDistCurve;
            var severity = rng.Range(0f, 1f) * S.Difficulty.Level;
            var dmgPct = Mathf.Lerp(0.15f, 0.4f, severity);
            var radius = Mathf.Lerp(5f, 9f, severity * 0.5f) + rng.Range(0f, 2f);
            radius *= fullness;
            toilet.Health.TakeDamage(toilet.Health.MaxHealth * dmgPct, DamageType.Acid);
            var toiletPos = toilet.PosIdx;
            Explosion.CreateCustom(S,
                (p) => AddDirtAt(S, rng, toiletPos, p, radius, curve),
                posIdx: toiletPos, radius: radius);
            Sounds.PlayExplosion(toilet.Position);
        }

        private static void AddDirtAt(GameState S, Rng rng, int centerPos,
                int pos, float radius, Curve sizeDistanceCurve) {
            var dirtSys = S.Sys.Dirt;
            // Blast force already stops behind walls; this only keeps dirt off
            // the wall cells themselves and off open space
            if (!EntityUtils.IsConstructedFloorAt(pos)) { return; }
            var wall = EntityUtils.WallAt(pos);
            if (wall != null && wall.IsConstructed && !Explosions.IsOpenDoorway(wall)) {
                return;
            }

            var dist = Pos.Distance(centerPos, pos);
            var distFromCenterPercent = dist / radius;
            // More density of dirt closer to the center, less further away.

            float amt = Mathf.Lerp(4f, 0.5f, distFromCenterPercent);
            int amtInt = Mathf.CeilToInt(amt);
            if (amtInt > 3) {
                amtInt = 3;
            } else if (amtInt < 1) {
                amtInt = 1;
            }
            for (int i = 0; i < amtInt; i++) {
                if (rng.Chance(1.1f - distFromCenterPercent)) {
                    string sprite;
                    if (rng.Chance(0.1f)) {
                        sprite = GraphicsId.DirtHumanWaste01;
                    } else {
                        sprite = GraphicsId.DirtHumanWaste02;
                    }
                    dirtSys.Add(DirtData.Create(pos, sprite,
                        rng.Range(0.9f, 1.1f)
                            * sizeDistanceCurve.Evaluate(distFromCenterPercent)));
                }
            }
        }
    }
}
