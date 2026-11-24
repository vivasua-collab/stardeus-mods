using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Firex {
    // The spray of a Firefoam Capsule: an explosion that only walls and closed doors stop
    // (ExplosionFlags.OnlyWallsBlock). It puts out every fire it reaches, briefly covers the
    // beings there, and leaves foam on every tile that can hold dirt. Nothing under the foam
    // catches fire until it is cleaned or wears off (FirexSys.IsFoamProtected).
    // Why it works this way: Docs/Architecture/Systems/Firefoam.md
    public static class FirefoamBurst {
        // How fast the foam front moves out in the shader, in tiles per second
        private const float SprayTilesPerSecond = 60f;
        // Beings the spray covers cannot catch fire for this long
        public const int BeingCoverTicks = 22;

        private static readonly List<int> fires = new();

        public static void Spray(GameState S, int center, float radius, Vector2 foamDays, Rng rng) {
            var now = S.Ticks;
            var firex = S.Sys.Firex;
            var dirt = S.Sys.Dirt;
            var centerPos = EntityUtils.CenterOf(center);
            Explosion.CreateCustom(S, (p) => {
                firex.ExtinguishAt(p);
                if (S.Beings.ByPos.TryGetValue(p, out var beings)) {
                    foreach (var b in beings) {
                        if (b.IsActive) {
                            b.Flammable.FoamCoverUntil = now + BeingCoverTicks;
                        }
                    }
                }
                var days = rng.Range(foamDays.x, foamDays.y);
                var expiresAt = now + (long) (days * Consts.TicksPerDay);
                var delay = Vectors.Distance(EntityUtils.CenterOf(p), centerPos)
                    / SprayTilesPerSecond;
                dirt.AddFoam(p, expiresAt, delay);
            }, posIdx: center, radius: radius,
                flags: ExplosionFlags.SkipFX | ExplosionFlags.OnlyWallsBlock);
        }

        // A fire the spray from center would reach, within radius. The fuse uses the spray's
        // own rule, so a capsule never pops for a fire behind a wall that its foam cannot reach
        public static bool AnyFireInReach(GameState S, int center, float radius) {
            S.Sys.Firex.CollectFiresWithin(center, radius, fires);
            for (int i = 0; i < fires.Count; i++) {
                if (Reaches(center, fires[i], radius)) {
                    return true;
                }
            }
            return false;
        }

        // Explosions.DealDamage reaches a cell when nothing stops the chain of steps from it,
        // one tile at a time towards the center. With OnlyWallsBlock only a wall on that chain
        // does; the target itself may be a wall (a burning wall gets put out)
        public static bool Reaches(int center, int target, float radius) {
            var centerPos = EntityUtils.CenterOf(center);
            if (Vectors.Distance(EntityUtils.CenterOf(target), centerPos) > radius) {
                return false;
            }
            var p = target;
            // every step gets a tile closer, the guard only protects against a stuck step
            for (int guard = 0; p != center && guard < 1000; guard++) {
                var prev = EntityUtils.ToPos(
                    Vector2.MoveTowards(EntityUtils.CenterOf(p), centerPos, 1f));
                if (prev != center && Explosions.IsWallInTheWay(prev)) {
                    return false;
                }
                p = prev;
            }
            return true;
        }
    }
}
