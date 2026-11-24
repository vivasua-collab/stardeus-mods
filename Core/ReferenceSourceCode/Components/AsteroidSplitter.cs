using Game.Commands;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public static class AsteroidSplitter {
        private const int MaxRetargetAttempts = 4;
        // Fragments keep the story event's promise to not land on game ending objects
        private const float ImportantTileMargin = 3f;

        public static void Split(GameState s, AsteroidComp parent, bool byShield) {
            var defs = The.Defs.Matching(parent.FragmentDef);
            if (Arrays.IsEmpty(defs)) {
                D.Err("No fragment defs matching {0} for {1}", parent.FragmentDef, parent.Obj);
                return;
            }
            var rng = s.Rng;
            var countRange = parent.FragmentCount;
            var count = Maths.Max(2, rng.Range((int) countRange.x, (int) countRange.y + 1));

            var parentPos = parent.Obj.Position;
            var parentVel = parent.Velocity;
            var parentDir = parentVel.normalized;
            var parentSpeed = parentVel.magnitude;
            var sideways = Vectors.Rotate(parentDir, 90f);
            var distLeft = Vectors.Distance(parentPos, parent.HitPosition);
            var descent = parent.DescentFraction;
            var ignoresForceField = byShield || parent.IgnoresForceField;

            var weights = new float[count];
            var weightSum = 0f;
            for (int i = 0; i < count; i++) {
                weights[i] = rng.Range(0.6f, 1.4f);
                weightSum += weights[i];
            }

            var spread = Tunables.AsteroidSplitSpreadDegrees;
            var step = spread / count;
            EntityUtils.LoadImportantTilesCache();
            for (int i = 0; i < count; i++) {
                // Area is what gets shared, so the pieces add up to less than the parent
                var radius = parent.Size * Mathf.Sqrt(
                    Tunables.AsteroidSplitMassKeep * weights[i] / weightSum);
                // -1..1 across the fan, keeps the pieces from stacking on one line
                var fanPos = ((i + 0.5f) / count) * 2f - 1f;
                var spawnPos = parentPos + sideways * (fanPos * parent.Size * 0.5f);
                if (!EntityUtils.IsWithinBounds(spawnPos)) {
                    spawnPos = parentPos;
                }
                var dir = parentDir;
                var hitPos = parent.HitPosition;
                for (int attempt = 0; attempt < MaxRetargetAttempts; attempt++) {
                    // The parent's own target is known to be safe, so every retry closes
                    // in on it rather than rolling the same unlucky slot of the fan again
                    var narrowing = 1f - attempt / (float) MaxRetargetAttempts;
                    var angle = (fanPos * spread * 0.5f + rng.Range(-step, step) * 0.35f)
                        * narrowing;
                    var tryDir = Vectors.Rotate(parentDir, angle);
                    var tryHit = spawnPos + tryDir * (distLeft * rng.Range(0.85f, 1.15f));
                    if (!EntityUtils.IsWithinBounds(tryHit)) {
                        tryHit = EntityUtils.ToSafe(tryHit);
                    }
                    if (!EntityUtils.IsTooCloseToAnyOf(Pos.FromVector(tryHit),
                            radius + ImportantTileMargin, Caches.TileList)) {
                        dir = (tryHit - spawnPos).normalized;
                        hitPos = tryHit;
                        break;
                    }
                }
                // Same point of the descent as the parent, or the piece would pop in size
                var startPos = hitPos + (spawnPos - hitPos) / descent;
                var velocity = dir * (parentSpeed * rng.Range(0.9f, 1.2f));

                var cmd = new CmdSpawnObj(spawnPos, rng.From(defs), true, false);
                cmd.Execute(s);
                if (cmd.Obj == null
                        || !cmd.Obj.TryGetComponent<AsteroidComp>(out var fragment)) {
                    D.Err("Failed to spawn asteroid fragment of {0}", parent.Obj);
                    continue;
                }
                fragment.InitAsFragment(radius, ShareOf(parent.Contents, count),
                    ignoresForceField, velocity, hitPos, startPos);
            }
        }

        private static Mat[] ShareOf(Mat[] contents, int count) {
            if (Arrays.IsEmpty(contents)) { return null; }
            var share = new Mat[contents.Length];
            for (int i = 0; i < contents.Length; i++) {
                var mat = contents[i];
                mat.StackSize = Maths.Max(1, mat.StackSize / count);
                share[i] = mat;
            }
            return share;
        }
    }
}
