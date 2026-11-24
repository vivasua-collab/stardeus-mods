using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Systems.AI;
using KL.Grid;
using UnityEngine;

namespace Game.Data {
    // Breaking a tile down drops one pile per material, so taking a derelict apart left
    // thousands of piles (2984 for the Pirates derelict), each one a hauling trip and a cost
    // every tick. A drop joins a pile of its type already lying in the same BlockSize block
    // instead, which leaves about a quarter of the piles. See PileScaling.md
    public static class DropMerge {
        public const int BlockSize = 3;
        private static readonly List<Obj> nearby = new();

        // The pile in pos's block a drop of type may join, or null
        public static UnstoredMatComp FindTarget(GameState s, MatType type, int amount,
                int pos) {
            Pos.ToXY(pos, out var x, out var y);
            var min = new Vector2(x - x % BlockSize, y - y % BlockSize);
            var max = min + new Vector2(BlockSize, BlockSize);
            nearby.Clear();
            s.Objs.QueryInRect(min, max, nearby, null);
            UnstoredMatComp best = null;
            var bestDist = int.MaxValue;
            for (int i = 0; i < nearby.Count; i++) {
                var pile = nearby[i].Pile;
                if (!CanJoin(pile, type, amount)) { continue; }
                var pilePos = pile.Entity.PosIdx;
                var dist = Pos.SqrDistance(pos, pilePos);
                if (dist >= bestDist) { continue; }
                // A pile behind a wall may be in another room, or out of reach
                if (IsWalledBetween(pos, pilePos)) { continue; }
                best = pile;
                bestDist = dist;
            }
            nearby.Clear();
            return best;
        }

        private static bool CanJoin(UnstoredMatComp pile, MatType type, int amount) {
            if (pile == null || pile.Type != type) { return false; }
            var ent = pile.Entity;
            if (!ent.IsActive || ent.IsCarriedByAny || !pile.IsAddedToInventory) {
                return false;
            }
            // A single item posing as an object, not a material stack
            if (pile.TreatAsObj) { return false; }
            // A fresh drop is never forbidden, it would quietly become forbidden too
            if (pile.IsForbidden) { return false; }
            if (pile.StackSize + amount > UnstoredMatComp.AbsoluteMaxStackSize) {
                return false;
            }
            // Someone is already on the way for this pile: what it planned to carry is
            // fixed, and the extra could be left without a storing order
            if (pile.Reserved > 0) { return false; }
            var goal = ent.Goal;
            if (goal != null && (goal.Agent != null
                    || goal.State == AIGoalState.PreAssigned
                    || goal.State == AIGoalState.InProgress)) {
                return false;
            }
            return true;
        }

        // Walls on the tiles stepped through from a to b, ends excluded
        private static bool IsWalledBetween(int a, int b) {
            Pos.ToXY(a, out var ax, out var ay);
            Pos.ToXY(b, out var bx, out var by);
            var steps = Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(by - ay));
            for (int i = 1; i < steps; i++) {
                var t = (float) i / steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(ax, bx, t));
                var y = Mathf.RoundToInt(Mathf.Lerp(ay, by, t));
                if (EntityUtils.WallAt(Pos.FromXY(x, y)) != null) { return true; }
            }
            // Diagonal neighbours: both corners walled means a sealed corner
            if (steps == 1 && ax != bx && ay != by) {
                return EntityUtils.WallAt(Pos.FromXY(ax, by)) != null
                    && EntityUtils.WallAt(Pos.FromXY(bx, ay)) != null;
            }
            return false;
        }
    }
}
