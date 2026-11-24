using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.BodyParts;
using Game.Systems.Combat;
using Game.Systems.Creatures;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    // A ranged shot flies on to where it ends: a miss can hit the target's cover, a bystander,
    // an ally or a wall, and even a shot on target can be caught by someone standing in the
    // line of fire. Players learn not to shoot past their own fighters, and lines of fire shape
    // positions. No AI avoids it (Tomas, 2026-10-12): it leaves positioning to the player.
    // A miss splits into a wild shot and a shot into cover, each with what it may hit
    // (Resolve); WildDestination picks where a wild shot lands, StopAtFirstWall keeps it on the
    // shooter's side of a wall, Trace finds what it hits on the way and Land what it hits where
    // it lands.
    public static class StrayShot {
        // nothing within 5 tiles of the shooter is intercepted, walls included, so the people
        // beside a shooter are safe and a shooter leaning past a corner does not shoot its own
        // wall; full chance from 12 tiles
        private const float SafeDistSqr = 25f;
        private const float FullDistSqr = 144f;
        // what stands where the shot lands is hit more often
        private const float LandingBeingChance = 0.5f;
        private const float LandingObjectMul = 1.5f;
        private const float LandingDownedMul = 0.5f;
        private const float LandingDownedMinDistSqr = 20.25f;
        // a wild shot can hit people other than its target only half the time
        private const float WildHitsPeopleChance = 0.5f;

        // What a shot can hit besides its target
        private const int People = 1;
        private const int World = 2;

        // Resolves a miss and returns where the shot ended, for the shot effect.
        // aim is the shooter's chance before the target's situation (ShotOdds.Aim), coverMul
        // what the target's cover left of it (ShotOdds.Cover), total the whole chance.
        public static Vector2 Resolve(GameState S, AttackMethod method, IAttackable attacker,
                FactionDef attackerFaction, Vector2 from, IAttackable target, float aim,
                float coverMul, float total) {
            var rng = Rng.Unseeded;
            if (coverMul < 1f && TryHitCover(S, method, attacker, attackerFaction, from, target,
                    aim, coverMul, total, rng, out var coverPos)) {
                return coverPos;
            }
            var dest = StopAtFirstWall(S, from,
                WildDestination(from, target.AttackablePosition, aim, rng));
            var hits = World;
            if (rng.Chance(WildHitsPeopleChance)) {
                hits |= People;
            }
            if (Trace(S, method, attacker, attackerFaction, from, dest, target, hits, rng,
                    out var stopped)) {
                return stopped;
            }
            Land(S, method, attacker, attackerFaction, from, dest, target, hits, rng);
            return dest;
        }

        // A shot on target can still be caught by someone in the line of fire beyond 5 tiles,
        // and by a wall on the way when the target is a wall or a door itself. True when it was
        // caught: the target is not hit.
        public static bool TryCatchOnTheWay(GameState S, AttackMethod method,
                IAttackable attacker, FactionDef attackerFaction, Vector2 from,
                IAttackable target, out Vector2 caughtAt) {
            var hits = People;
            if (!(target is CombatUnit)
                    && S.Query.GetCoverMap.Ask()[target.AttackablePosIdx] > 99) {
                hits |= World;
            }
            return Trace(S, method, attacker, attackerFaction, from, target.AttackablePosition,
                target, hits, Rng.Unseeded, out caughtAt);
        }

        // Aim is rolled first (a miss there goes wild) and the cover second (a miss there hits
        // the cover). Of all the misses, this is the share the cover took. The shot flies
        // to the cover and can be caught on the way like any other.
        private static bool TryHitCover(GameState S, AttackMethod method, IAttackable attacker,
                FactionDef attackerFaction, Vector2 from, IAttackable target,
                float aim, float coverMul, float total, Rng rng, out Vector2 pos) {
            pos = default;
            var missChance = 1f - total;
            if (missChance <= 0f) { return false; }
            var coverShare = math.saturate(aim * (1f - coverMul) / missChance);
            if (!rng.Chance(coverShare)) { return false; }
            // the tile that blocks the most of this shot
            CoverUtils.BlockChance(S, target.AttackablePosIdx, Pos.FromVector(from),
                out var coverIdx);
            if (coverIdx == Pos.Invalid) { return false; }
            var cover = CoverTileAt(coverIdx);
            if (cover == null) { return false; }
            var coverCenter = EntityUtils.CenterOf(coverIdx);
            if (Trace(S, method, attacker, attackerFaction, from, coverCenter, target,
                    People | World, rng, out pos)) {
                return true;
            }
            DamageCover(cover, coverIdx, method, target);
            pos = coverCenter;
            return true;
        }

        // 1 tile off for a good shot, up to 10 for a hopeless one, never behind the shooter
        private static Vector2 WildDestination(Vector2 from, Vector2 targetPos, float aim,
                Rng rng) {
            missRadiusCurve ??= CurveDef.FastGet(CurveIdH.Attacks_StrayShot_MissRadius);
            var maxDist = missRadiusCurve.Evaluate(math.saturate(aim));
            var dist = math.lerp(1f, maxDist, rng.Value);
            var forward = targetPos - from;
            var dest = targetPos;
            for (int i = 0; i < 8; i++) {
                var angle = rng.Value * 2f * math.PI;
                dest = targetPos + new Vector2(math.cos(angle), math.sin(angle)) * dist;
                if (Vector2.Dot(forward, dest - from) >= 0f) { break; }
            }
            return dest;
        }
        private static Curve missRadiusCurve;

        // A wild shot that would land where the shooter cannot see ends at the first wall or
        // closed door on its way, the one right beside the shooter too. Nothing within 5 tiles
        // stops a shot in Trace, so a gunner standing near a wall put its misses through it into
        // whatever stood behind (STA-1755).
        private static Vector2 StopAtFirstWall(GameState S, Vector2 from, Vector2 dest) {
            if (EntityUtils.HasLineOfSightCover(from, dest, 0f, 0f, out _)) { return dest; }
            var coverMap = S.Query.GetCoverMap.Ask();
            var walk = new TileWalk(from, dest);
            while (walk.Next()) {
                if (!EntityUtils.IsWithinBounds(walk.X, walk.Y)) { break; }
                var pos = Pos.FromXY(walk.X, walk.Y);
                if (coverMap[pos] > EntityUtils.SightBlockingCover) {
                    return EntityUtils.CenterOf(pos);
                }
            }
            return dest;
        }

        private static Tile CoverTileAt(int posIdx) {
            var obj = EntityUtils.ParentObjAt(posIdx);
            if (obj?.IsConstructed == true && obj.Health != null) { return obj; }
            var wall = EntityUtils.WallAt(posIdx);
            if (wall?.IsConstructed == true && wall.Health != null) { return wall; }
            return null;
        }

        // Intercepts along the tiles from the shooter to dest, the destination tile left out
        // (as EntityUtils.HasLineOfSightCover walks them). True when something on the way
        // stopped the shot, with where.
        private static bool Trace(GameState S, AttackMethod method, IAttackable attacker,
                FactionDef attackerFaction, Vector2 from, Vector2 dest, IAttackable target,
                int hits, Rng rng, out Vector2 stopped) {
            stopped = dest;
            var coverMap = S.Query.GetCoverMap.Ask();
            var walk = new TileWalk(from, dest);
            var destIdx = walk.DestIdx;
            while (walk.Next()) {
                if (!EntityUtils.IsWithinBounds(walk.X, walk.Y)) { return false; }
                var pos = Pos.FromXY(walk.X, walk.Y);
                if (pos == destIdx) { return false; }
                var center = EntityUtils.CenterOf(pos);
                var distSqr = Vectors.SqrDistance(from, center);
                // the distance comes first: next to the shooter nothing stops a shot
                var distFactor = InterceptFactor(distSqr);
                if (distFactor <= 0f) { continue; }
                var cover = coverMap[pos];
                if (cover > 99) {
                    // a wall, a closed door or a full height object: always when the shot may
                    // hit the world, and always when it is the wall the shot was fired at
                    if ((hits & World) != 0 || pos == target.AttackablePosIdx) {
                        DamageCover(CoverTileAt(pos), pos, method, target);
                        stopped = center;
                        return true;
                    }
                    continue;
                }
                if ((hits & People) != 0 && TryHitBeingAt(S, pos, method, attacker,
                        attackerFaction, target, rng, distSqr, landing: false)) {
                    stopped = center;
                    return true;
                }
                if ((hits & World) != 0 && cover > 20) {
                    // cover right by where the shot lands is in its way, cover on the way
                    // only grazes it
                    var mul = Pos.AreAdjacent(pos, destIdx)
                        ? 1f : Tunables.CombatStrayShotObjectInterceptMul;
                    if (rng.Chance(cover / 100f * mul * distFactor)) {
                        DamageCover(CoverTileAt(pos), pos, method, target);
                        stopped = center;
                        return true;
                    }
                }
            }
            return false;
        }

        // A shot that lands on a tile: whoever or whatever stands there may take it
        private static void Land(GameState S, AttackMethod method, IAttackable attacker,
                FactionDef attackerFaction, Vector2 from, Vector2 dest, IAttackable target,
                int hits, Rng rng) {
            var destIdx = Pos.FromVector(dest);
            if (!EntityUtils.IsWithinBounds(destIdx)) { return; }
            if ((hits & People) != 0 && TryHitBeingAt(S, destIdx, method, attacker,
                    attackerFaction, target, rng, Vectors.SqrDistance(from, dest),
                    landing: true)) {
                return;
            }
            var landCover = S.Query.GetCoverMap.Ask()[destIdx];
            if (landCover > 0 && rng.Chance(landCover / 100f * LandingObjectMul)) {
                DamageCover(CoverTileAt(destIdx), destIdx, method, target);
            }
        }

        // A stray shot only chips a wall or a door, so misses do not breach the ship. The wall
        // the shot was fired at takes it in full.
        private static void DamageCover(Tile tile, int posIdx, AttackMethod method,
                IAttackable target) {
            if (tile == null) { return; }
            var damage = method.Damage;
            if (tile.Definition.LayerId == WorldLayer.Walls
                    && posIdx != target.AttackablePosIdx) {
                damage *= Tunables.CombatStrayShotWallDamageMul;
            }
            tile.Health.TakeDamage(damage, method.Type);
        }

        private static float InterceptFactor(float distSqr) {
            return math.saturate(math.unlerp(SafeDistSqr, FullDistSqr, distSqr));
        }

        private static bool TryHitBeingAt(GameState S, int pos, AttackMethod method,
                IAttackable attacker, FactionDef attackerFaction, IAttackable target, Rng rng,
                float distSqr, bool landing) {
            if (!S.Beings.ByPos.TryGetValue(pos, out var beings)) { return false; }
            var distFactor = InterceptFactor(distSqr);
            var attackerArmy = (attacker as CombatUnit)?.Army;
            foreach (var b in beings) {
                if (!b.IsActive || !b.IsAlive) { continue; }
                var unit = b.AIAgent?.CombatUnit;
                if (unit == null || unit == target || unit == attacker) { continue; }
                var friendly = IsFriendly(S, attackerFaction, attackerArmy, unit);
                var lying = b.Data.Body.IsIncapacitated
                    || b.Data.HasCondition(ConditionIdH.Sleeping);
                float chance;
                if (landing) {
                    chance = LandingBeingChance * SizeFactor(b.Data);
                    if (lying && distSqr >= LandingDownedMinDistSqr) {
                        chance *= LandingDownedMul;
                    }
                    if (friendly) {
                        chance *= distFactor;
                    }
                } else {
                    chance = Tunables.CombatStrayShotInterceptChance * SizeFactor(b.Data);
                    if (lying) {
                        chance *= Tunables.CombatStrayShotDownedMul;
                    }
                    if (friendly) {
                        // an ally in the line of fire is hit less often
                        chance *= Tunables.CombatStrayShotFriendlyMul;
                    }
                    chance *= distFactor;
                }
                if (rng.Chance(chance)) {
                    AttackAction.DealDamageTo(S, method, attacker, unit);
                    if (!friendly) {
                        // a stray hit from an enemy is an attack like any other
                        unit.NoticeAttack(attacker);
                    }
                    return true;
                }
            }
            return false;
        }

        // the chance to be hit scales with body size (a human is 1)
        private static float SizeFactor(BeingData b) {
            switch (b.Species.Size) {
                case BodySize.Small: return 0.5f;
                case BodySize.Large: return 1.5f;
                default: return 1f;
            }
        }

        // Friendly fire: anyone not hostile to the shooter's faction. Combat here is
        // between armies, so the shooter's army counts (a guest sheltering with the crew), and
        // a faction that is not hostile to the crew counts for the crew's shots and the other
        // way around. Wild animals have no faction to spare.
        private static bool IsFriendly(GameState S, FactionDef attackerFaction,
                Army attackerArmy, CombatUnit unit) {
            if (attackerFaction == null) { return false; }
            if (attackerArmy != null && unit.Army == attackerArmy) { return true; }
            var data = unit.Data;
            if (data.Species.IsWild) { return false; }
            var faction = data.EffectiveFaction;
            if (faction == attackerFaction) { return true; }
            if (attackerFaction.IsPlayer) {
                return !S.Sys.Diplomacy.IsHostileTowardsPlayer(faction);
            }
            if (faction != null && faction.IsPlayer) {
                return !S.Sys.Diplomacy.IsHostileTowardsPlayer(attackerFaction);
            }
            return false;
        }

        // The tiles a shot's line crosses from the shooter's tile to the destination tile, in
        // order, the shooter's own tile left out (as EntityUtils.HasLineOfSightCover walks them)
        private struct TileWalk {
            private readonly int stepX;
            private readonly int stepY;
            private readonly int x1;
            private readonly int y1;
            private readonly float tDeltaX;
            private readonly float tDeltaY;
            private float tMaxX;
            private float tMaxY;
            private int steps;
            public int X;
            public int Y;
            public int DestIdx => Pos.FromXY(x1, y1);

            public TileWalk(Vector2 from, Vector2 to) {
                float dx = to.x - from.x;
                float dy = to.y - from.y;
                X = (int) math.floor(from.x);
                Y = (int) math.floor(from.y);
                x1 = (int) math.floor(to.x);
                y1 = (int) math.floor(to.y);
                stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
                stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
                tMaxX = stepX != 0 ? ((stepX > 0 ? X + 1f : X) - from.x) / dx : float.MaxValue;
                tMaxY = stepY != 0 ? ((stepY > 0 ? Y + 1f : Y) - from.y) / dy : float.MaxValue;
                tDeltaX = stepX != 0 ? math.abs(1f / dx) : float.MaxValue;
                tDeltaY = stepY != 0 ? math.abs(1f / dy) : float.MaxValue;
                steps = 0;
            }

            // Steps to the next tile; false once the destination tile was the last one
            public bool Next() {
                if ((X == x1 && Y == y1) || steps++ >= 512) { return false; }
                if (tMaxX < tMaxY) {
                    X += stepX;
                    tMaxX += tDeltaX;
                } else {
                    Y += stepY;
                    tMaxY += tDeltaY;
                }
                return true;
            }
        }
    }
}
