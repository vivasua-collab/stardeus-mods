using System;
using System.Buffers;
using Game.Constants;
using Game.Data;
using Game.Systems.Reg;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    // Which door a unit breaks when no open way leads to its enemy. Like RW raiders, it plans
    // the way as if doors could be broken at a price and breaks the first one on it, so an
    // open way around beats a door that is only a little closer. Picking the nearest denied
    // door anywhere had raiders bash a second airlock while the player held the first one open
    // for them, and bash doors that led away from their target (STA-1737)
    public static class BreachRoute {
        public enum Way : byte {
            // no enemy to go for, or only walls in between
            None,
            // an enemy can be walked to
            Open,
            // breaking the door is the way to the enemy
            Door,
        }

        private const int MaxEnemyCandidates = 8;
        private static readonly int ReplanCooldownIdH = Hashes.S("BreachReplan");

        // A unit bashing a door looks again now and then: a door opened elsewhere, or the enemy
        // came where it can be reached. It used to bash on until the door broke
        public static bool IsReplanDue(AIAgentComp agent, long ticks) {
            if (agent.BB.IsCoolingDown(ReplanCooldownIdH, ticks)) { return false; }
            DelayReplan(agent, ticks);
            return true;
        }

        // right after a unit picked its door
        public static void DelayReplan(AIAgentComp agent, long ticks) {
            agent.BB.SetCooldown(ReplanCooldownIdH, ticks, Consts.TicksPer10Minutes,
                Consts.TicksPer15Minutes);
        }

        public static Way Toward(GameState S, AIAgentComp agent, int targetPos, out Tile door) {
            door = null;
            var motion = agent.Motion;
            var req = RegPathReq.Create(agent.PosIdx, targetPos, motion.IsGrounded, true);
            req.Permissions = motion.Permissions;
            var pos = S.Query.RegionBreachDoor.Ask(req);
            if (pos == 0) { return Way.Open; }
            if (pos < 0) { return Way.None; }
            var wall = EntityUtils.WallAt(pos);
            if (wall?.Door == null || !wall.IsConstructed) { return Way.None; }
            door = wall;
            return Way.Door;
        }

        // Open when any enemy can be walked to. Otherwise the way to the enemy that would be
        // nearest with every door open
        public static Way TowardNearestEnemy(GameState S, AIAgentComp agent, out Tile door) {
            door = null;
            var cu = agent.CombatUnit;
            var enemies = RentEnemyCandidates(S, cu, out var cnt);
            if (cnt == 0) {
                ArrayPool<CombatUnit>.Shared.Return(enemies, true);
                return Way.None;
            }
            var motion = agent.Motion;
            var reachable = RegUtils.FindClosestReachable(S, cu.PosIdx, motion.IsGrounded,
                enemies, cnt, e => e.PosIdx, permissions: motion.Permissions);
            CombatUnit nearest = null;
            if (reachable == null) {
                nearest = RegUtils.FindClosestReachable(S, cu.PosIdx, motion.IsGrounded,
                    enemies, cnt, e => e.PosIdx);
            }
            ArrayPool<CombatUnit>.Shared.Return(enemies, true);
            if (reachable != null) { return Way.Open; }
            if (nearest == null) { return Way.None; }
            return Toward(S, agent, nearest.PosIdx, out door);
        }

        // Breaking the door opens nothing new: every side of it can be reached already, like an
        // airlock into a room that another, open airlock leads into
        public static bool LeadsNowhereNew(GameState S, AIAgentComp agent, Tile door) {
            var motion = agent.Motion;
            var areas = S.Sys.Areas;
            var p = door.PosIdx;
            for (int i = 0; i < 4; i++) {
                var np = Pos.NeighborAt(p, i);
                if (Pos.IsOOB(np) || areas.At(np) == -1) { continue; }
                var req = RegPathReq.To(agent.PosIdx, np, motion.IsGrounded);
                req.Permissions = motion.Permissions;
                if (S.Query.RegionDist.Ask(req) < 0) { return false; }
            }
            return true;
        }

        // Live units of the other armies, the 8 closest by straight line when there are more.
        // Return the array to ArrayPool<CombatUnit>.Shared
        public static CombatUnit[] RentEnemyCandidates(GameState S, CombatUnit cu, out int count) {
            count = 0;
            var pool = ArrayPool<CombatUnit>.Shared;
            var myArmy = cu.Army;
            var enemies = pool.Rent(Math.Max(1, S.Beings.AliveCount - 1));
            if (myArmy == null) { return enemies; }
            var armies = S.Query.GetArmies.Ask();
            for (int i = armies.Count - 1; i >= 0; i--) {
                Army a = armies[i];
                if (myArmy == a) { continue; }
                for (int i1 = a.Units.Count - 1; i1 >= 0; i1--) {
                    CombatUnit u = a.Units[i1];
                    if (u.State != CombatUnitState.Incapacitated && count < enemies.Length) {
                        enemies[count++] = u;
                    }
                }
            }
            if (count > MaxEnemyCandidates) {
                Array.Sort(enemies, 0, count, new EuclidDistComparer(cu.Position));
                count = MaxEnemyCandidates;
            }
            return enemies;
        }

        private struct EuclidDistComparer : System.Collections.Generic.IComparer<CombatUnit> {
            private Vector2 origin;
            public EuclidDistComparer(Vector2 origin) { this.origin = origin; }
            public int Compare(CombatUnit a, CombatUnit b) {
                if (a == null && b == null) { return 0; }
                if (a == null) { return 1; }
                if (b == null) { return -1; }
                float da = Vectors.SqrDistance(origin, a.Position);
                float db = Vectors.SqrDistance(origin, b.Position);
                return da.CompareTo(db);
            }
        }
    }
}
