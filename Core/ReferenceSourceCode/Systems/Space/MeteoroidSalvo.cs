using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using KL.Randomness;

namespace Game.Systems.Space {
    public static class MeteoroidSalvo {
        public static void Spawn(GameState s, long ticks, Facing.Type from,
                int count, float angleVar, Rng rng) {
            // count + 1 divisions puts every slot strictly inside the edge, so no
            // meteoroid spawns in a corner - this is what the old i=1..cnt-1 loop
            // was working around ("this is for a reason, to get the right position")
            var posF = EntityUtils.MeteorVectorOOB(from, angleVar, count + 1, rng,
                Tunables.MeteorOOBSpawnDistance, out var strikeV);
            // Midpoint of MeteorComp.OnConfig velocityRange [0.8,1.5]*5 for approach speed
            var approachVel = strikeV * 5.75f * EntityUtils.MapSizeRatio;
            // Defs.Matching regex-scans every def in the game - roll from one list
            var defs = The.Defs.Matching(DefIds.ObjAsteroidsMeteor01);
            for (int i = 1; i <= count; i++) {
                var velMul = rng.Chance(0.75f) ? rng.Range(0.25f, 0.5f) : rng.Range(0.25f, 0.75f);
                s.Sys.PendingBodies.Enqueue(new PendingSpaceBody {
                    Pos = posF(i),
                    Velocity = approachVel * velMul,
                    StrikeV = strikeV,
                    DefId = rng.From(defs).Id,
                    VelMul = velMul,
                    ExpireTick = ticks + Consts.TicksPer6Hours,
                    IsAsteroid = false,
                });
            }
        }
    }
}
