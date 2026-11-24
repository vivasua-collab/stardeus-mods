using Game.Data;
using KL.Grid;
using Unity.Mathematics;

namespace Game.Systems.AI.Combat {
    // How much of a shot the cover around its target blocks. Every tile next to the target
    // counts by how squarely the shot comes past it: in full within 15 degrees, nothing past
    // 65, a diagonal neighbor as if 1.75 times further off. Cover close to the shooter counts
    // for less (a third within 1.9 tiles, two thirds within 2.9), so firing over one's own
    // sandbag does not hand it to the enemy, and two units at one sandbag share it.
    // Leaning around a corner follows from the same rule: the corner tile beside the shooter
    // still covers it against a shot that comes past the corner.
    // It replaced the target's CoverSlot in the hit chance, which quantized the shot to one of
    // 8 directions; CoverSlot still ranks tiles in the cover searches.
    public static class CoverUtils {
        // a wall or other full height thing blocks 75%, anything lower its fill
        public const float FullCoverBlock = 0.75f;

        // 0..1, the share of a shot from shooterPosIdx the target's cover blocks
        public static float BlockChance(GameState S, int targetPosIdx, int shooterPosIdx) {
            return BlockChance(S, targetPosIdx, shooterPosIdx, out _);
        }

        // bestCoverIdx: the tile that blocks the most of it, where a shot the cover stops lands
        // (Pos.Invalid without cover)
        public static float BlockChance(GameState S, int targetPosIdx, int shooterPosIdx,
                out int bestCoverIdx) {
            bestCoverIdx = Pos.Invalid;
            if (targetPosIdx == shooterPosIdx) { return 0f; }
            float best = 0f;
            var coverMap = S.Query.GetCoverMap.Ask();
            Pos.ToXY(targetPosIdx, out var tx, out var ty);
            Pos.ToXY(shooterPosIdx, out var sx, out var sy);
            var shotAngle = math.degrees(math.atan2(sy - ty, sx - tx));
            float block = 0f;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    if (dx == 0 && dy == 0) { continue; }
                    int ax = tx + dx;
                    int ay = ty + dy;
                    if (!EntityUtils.IsWithinBounds(ax, ay)) { continue; }
                    var adj = Pos.FromXY(ax, ay);
                    if (adj == shooterPosIdx) { continue; }
                    var cover = coverMap[adj];
                    if (cover == 0) { continue; }
                    var b = BlockOf(cover, dx, dy, shotAngle, ax - sx, ay - sy);
                    if (b > best) {
                        best = b;
                        bestCoverIdx = adj;
                    }
                    block += (1f - block) * b;
                }
            }
            return block;
        }

        // the angle of each neighbor from the target, [dx + 1, dy + 1]: the cover AI asks every
        // unit tick, so only the shot's own angle is worked out per call
        private static readonly float[,] neighborAngles = NeighborAngles();
        private static float[,] NeighborAngles() {
            var angles = new float[3, 3];
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    angles[dx + 1, dy + 1] = math.degrees(math.atan2(dy, dx));
                }
            }
            return angles;
        }

        // the share of the shot one neighbor blocks, by the angle the shot passes it at and
        // the shooter's distance to it
        private static float BlockOf(byte cover, int dx, int dy, float shotAngle,
                int fromShooterX, int fromShooterY) {
            var angle = AngleDiff(neighborAngles[dx + 1, dy + 1], shotAngle);
            if (dx != 0 && dy != 0) {
                angle *= 1.75f;
            }
            float b = cover > 99 ? FullCoverBlock : cover / 100f;
            if (angle < 15f) {
                // in full
            } else if (angle < 27f) {
                b *= 0.8f;
            } else if (angle < 40f) {
                b *= 0.6f;
            } else if (angle < 52f) {
                b *= 0.4f;
            } else if (angle < 65f) {
                b *= 0.2f;
            } else {
                return 0f;
            }
            var distToShooter = math.sqrt(fromShooterX * fromShooterX
                + fromShooterY * fromShooterY);
            if (distToShooter < 1.9f) {
                b *= 0.3333f;
            } else if (distToShooter < 2.9f) {
                b *= 0.66666f;
            }
            return b;
        }

        private static float AngleDiff(float a, float b) {
            var d = math.abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }
    }
}
