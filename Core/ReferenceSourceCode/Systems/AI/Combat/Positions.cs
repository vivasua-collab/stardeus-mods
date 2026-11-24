#if !PRODUCTION_BUILD
// #define DEBUG_POSITIONS
#endif
using System.Buffers;
using Game.Components;
using Game.Data;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Systems.Reg;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    public static class Positions {
        public static bool TryFindCoverNear(GameState S, AIAgentComp agent,
                int nearPosIdx, float maxDist) {
            if (!agent.CombatUnit.IsTactical) { return false; }
            if (nearPosIdx < 1) {
                D.Err("Trying to find cover near invalid pos: {0}", nearPosIdx);
                return false;
            }
            #if DEBUG_POSITIONS
            D.Warn("Trying to find cover near: {0} within {1} for {2}",
                Pos.String(nearPosIdx), maxDist, agent);
            #endif
            var cover = S.Query.FindCoverNear.Ask(agent.CombatUnit, nearPosIdx, maxDist);
            if (cover.Total > 0) {
                agent.BB.MoveTargetPos = cover.PosIdx;
                if (agent.Motion.TryGoTo(cover.PosIdx, PathReqFlags.AdjOnTop,
                        out agent.BB.MoveProgress)) {
                    agent.CombatUnit.RecordCoverVisit(cover.PosIdx);
                    return true;
                } else {
                    agent.Motion.StoreNavFailureAt(cover.PosIdx);
                    agent.BB.ClearMoveTarget();
                }
            }
            return false;
        }

        public static bool TryFindCover(GameState S, AIAgentComp agent, bool best) {
            // Bots don't think about cover (CombatUnit.IsTactical). Every cover search goes
            // through here or TryFindCoverNear, so shared callers can't send one there.
            if (!agent.CombatUnit.IsTactical) { return false; }
            #if DEBUG_POSITIONS
            D.Warn("Trying to find cover (best: {0}) for {1}", best, agent);
            #endif
            var cover = S.Query.FindCover.Ask(agent.CombatUnit, best);
            if (cover.Total > 0) {
                agent.BB.MoveTargetPos = cover.PosIdx;
                // D.Warn("Trying to go to cover: {0} -> {1}", Pos.String(cover.PosIdx), agent);
                if (agent.Motion.TryGoTo(cover.PosIdx, PathReqFlags.AdjOnTop,
                        out agent.BB.MoveProgress)) {
                    agent.CombatUnit.RecordCoverVisit(cover.PosIdx);
                    return true;
                } else {
                    agent.Motion.StoreNavFailureAt(cover.PosIdx);
                    agent.BB.ClearMoveTarget();
                }
            }
            return false;
        }

        public static bool TryLeaveBadCombatPos(GameState S, AIAgentComp agent) {
            var path = S.Sys.Path;
            var isGrounded = agent.Motion.IsGrounded;
            var beingId = agent.Being.Id;
            int found = Pos.Around(agent.PosIdx, Caches.PosIdx9, false);
            for (int i = 0; i < found; i++) {
                var pos = Caches.PosIdx9[i];
                if (agent.Motion.HadNavFailureAt(pos)) { continue; }
                if (!path.IsNavigable(pos, isGrounded)) { continue; }
                if (path.IsSpotTaken(pos, beingId)) { continue; }
                if (S.Beings.CountAt(pos) > 0) { continue; }
                if (CombatUnit.IsBadPos(S, pos)) { continue; }

                agent.BB.MoveTargetPos = pos;
                if (agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop,
                        out agent.BB.MoveProgress)) {
                    return true;
                }
                agent.Motion.StoreNavFailureAt(pos);
                agent.BB.ClearMoveTarget();
            }
            return false;
        }

        public static bool IsGoodPosFor(GameState S, CombatUnit unit, int pos) {
            if (unit.Agent.Motion.HadNavFailureAt(pos)) {
                return false;
            }
            if (pos == unit.PosIdx) {
                if (unit.IsInBadPos) {
                    return false;
                }
            } else {
                if (S.Beings.CountAt(pos) > 0) {
                    return false;
                }
            }
            if (!S.Sys.Path.IsGoodCoverAt(pos, unit.Data.Being.Id)) {
                return false;
            }
            var candidatePos = Pos.ToVector(pos);
            var range = unit.ClosestRange;
            if (range > 0f) {
                // Measure from the candidate tile, not the unit's current position -
                // otherwise every candidate is rejected once any hostile is already
                // within range of where the unit is standing right now.
                var enemyTooClose = S.Sys.Combat.FirstEnemyWithin(unit, candidatePos,
                    range * range);
                if (enemyTooClose != null) {
                    // D.Warn("Enemy too close to {0}: {1} / {2}",
                    //     Pos.StringFromTo(unit.PosIdx, enemyTooClose.PosIdx),
                    //     unit, enemyTooClose);
                    return false;
                }
            }
            // A "region" (RegionPositionsAt) is a pathfinding concept - a walkable patch that
            // can span an entire open room, or several rooms joined by a doorway. It says
            // nothing about visual line of sight, which is tracked by a separate system
            // (CoverSys.coverSlotMap) and can be blocked mid-region by a wall, device, or other
            // obstruction. Without this check, a "well covered" candidate could be sitting in a
            // different room with no sightline to the target at all.
            if (unit.BestAttack?.IsMelee == false) {
                Vector2? targetPos = unit.AttackTarget != null
                    ? unit.AttackTarget.SightPosition
                    : unit.Attackable?.AttackablePosition;
                if (targetPos != null
                        && !EntityUtils.HasLineOfSightCoverFast(candidatePos, targetPos.Value, 0f, CombatUnit.SightRange)) {
                    return false;
                }
                // Cover the gun can't shoot from is only a place to wait out the fight: a raider
                // took cover far back, walked up again, and back, all fight long
                var reach = unit.BestAttack.Stats.RangeMax.Value;
                if (targetPos != null
                        && Vectors.SqrDistance(candidatePos, targetPos.Value) > reach * reach) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// When normal pathing to an attack target has repeatedly failed (e.g. a locked or
        /// permission-denied door blocks every route), find the door to bash/shoot down instead
        /// of standing still forever: the first one on the way to the target (BreachRoute), or
        /// without a target, the nearest one on the outline of the unit's current area. Applies
        /// to melee and ranged units alike.
        /// </summary>
        public static Tile FindBlockingDoor(GameState S, AIAgentComp agent) {
            // the door on the way to the target, not the nearest one: a pirate in the crew's
            // hall bashed into the bots' room while its target fled to the dorm (STA-1737)
            var target = agent.CombatUnit.AttackTarget;
            if (target != null) {
                switch (BreachRoute.Toward(S, agent, target.PosIdx, out var door)) {
                    case BreachRoute.Way.Door:
                        return door;
                    case BreachRoute.Way.Open:
                        // nothing to break: the target can be walked to
                        return null;
                }
            }
            var areas = S.Sys.Areas;
            var curAreaId = areas.At(agent.PosIdx);
            Caches.IntSet.Clear();
            areas.OutlineOfArea(curAreaId, Caches.IntSet);
            Caches.TileSet.Clear();
            var motion = agent.Motion;
            if (Caches.IntSet.Count > 0) {
                foreach (var p in Caches.IntSet) {
                    var wall = EntityUtils.WallAt(p);
                    if (wall == null || !wall.IsConstructed) { continue; }
                    var door = wall.Door;
                    if (door == null || (!door.IsLocked && !door.IsDeniedFor(motion))) { continue; }
                    if (BordersDifferentArea(areas, curAreaId, p)) {
                        Caches.TileSet.Add(wall);
                    }
                }
            } else if (curAreaId != -1) {
                // Space (and a non-landable outer area) has no outline, so a raider in space
                // held off by an airlock never found it to bash (STA-1657). Any door touching
                // the unit's own area qualifies, which keeps it to the exterior doors. Not for
                // -1 (walls): every door touches a wall
                var doors = S.Components.AllByType<DoorComp>();
                if (doors != null) {
                    foreach (DoorComp door in doors) {
                        var wall = door.Tile;
                        if (!wall.IsConstructed) { continue; }
                        if (!door.IsLocked && !door.IsDeniedFor(motion)) { continue; }
                        var p = wall.PosIdx;
                        if (TouchesArea(areas, curAreaId, p) && BordersDifferentArea(areas, curAreaId, p)) {
                            Caches.TileSet.Add(wall);
                        }
                    }
                }
            }
            if (Caches.TileSet.Count == 0) { return null; }
            var pool = ArrayPool<Tile>.Shared;
            var tiles = pool.Rent(Caches.TileSet.Count);
            int i = 0;
            foreach (var t in Caches.TileSet) { tiles[i++] = t; }
            var closest = RegUtils.FindClosestReachable(S, agent.PosIdx, motion.IsGrounded,
                tiles, i, t => t.PosIdx, onTop: false);
            pool.Return(tiles, true);
            return closest;
        }

        // A door being bashed that was unlocked, or stopped denying the unit (held open by a
        // body), is a way in now. Bashing on wasted the raid on it
        public static bool NoLongerBlocks(Tile tile, MotionControl motion) {
            var door = tile.Door;
            return door != null && !door.IsLocked && !door.IsDeniedFor(motion);
        }

        private static bool TouchesArea(AreaSys areas, int areaId, int p) {
            return areas.At(Pos.E(p)) == areaId || areas.At(Pos.W(p)) == areaId
                || areas.At(Pos.S(p)) == areaId || areas.At(Pos.N(p)) == areaId;
        }

        // mirrors AICombatActionBreakIn.TryAddDoor
        private static bool BordersDifferentArea(AreaSys areas, int curAreaId, int p) {
            var id = areas.At(Pos.E(p));
            if ((id > 0 || id < -1) && id != curAreaId) { return true; }
            id = areas.At(Pos.W(p));
            if ((id > 0 || id < -1) && id != curAreaId) { return true; }
            id = areas.At(Pos.S(p));
            if ((id > 0 || id < -1) && id != curAreaId) { return true; }
            id = areas.At(Pos.N(p));
            return (id > 0 || id < -1) && id != curAreaId;
        }
    }
}
