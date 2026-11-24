using System;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    // Where a shooter goes to fire from: within reach of the target with sight to it, close to 80% of the gun's reach
    // (AttackMethod.PreferredDist), with cover toward the target, and not far from where the
    // shooter stands. The accuracy peak plays no part: walking to it put short guns a few tiles
    // from their targets, where the punch takes over.
    public static class CastPosition {
        // how far from the shooter a spot may be; farther targets are closed in on first
        private const float MaxSearchDist = 12f;
        // each tile walked costs this share of the score
        public const float WalkDecayPerTile = 0.967f;
        // A spot is worth this much less for each of its 8 neighbors another being stands on
        // or walks to. RW has no such rule: its pawns spread only by reserving destinations
        // and by picking spots near themselves, and its area damage punishes a clump. Ground
        // combat here has no area damage, so units filled every tile along one wall. Cover
        // still comes first: full cover beside an ally (0.85 x 0.6) beats an open spot alone (0.3).
        public const float ElbowRoomMul = 0.6f;
        // spots this close to the target are worth half
        private const float CloseToTargetDist = 5f;
        // the expensive checks (reachability, enemies nearby, sight) stop after this many
        private const int MaxValidated = 40;

        private static readonly float[] scores = new float[1024];
        private static readonly int[] candidates = new int[1024];

        // The best spot to fire at the target from, or -1. useCover is false for units that
        // do not think about cover (bots) and for spots picked for the player.
        public static int Find(GameState S, CombatUnit cu, int targetPosIdx,
                Vector2 targetSightPos, bool useCover, out float bestScore) {
            bestScore = 0f;
            var attack = cu.BestAttack;
            if (attack == null || attack.IsMelee) { return -1; }
            var rangeMax = attack.Stats.RangeMax.Value;
            var minDist = Mathf.Max(cu.ClosestRange, attack.Stats.RangeMin.Value);
            Pos.ToXY(targetPosIdx, out var tx, out var ty);
            int found = Pos.OuterRadius(cu.PosIdx, 0.5f, MaxSearchDist, candidates);
            int n = 0;
            var minSqr = minDist * minDist;
            var maxSqr = rangeMax * rangeMax;
            for (int i = 0; i < found; i++) {
                var p = candidates[i];
                var dSqr = Pos.SqrDistance(p, targetPosIdx);
                if (dSqr < minSqr || dSqr > maxSqr) { continue; }
                candidates[n] = p;
                // sorted ascending below, so the best comes first
                scores[n] = -Score(S, cu, attack, p, targetPosIdx, tx, ty, useCover);
                n++;
            }
            if (n == 0) { return -1; }
            Array.Sort(scores, candidates, 0, n);
            var path = S.Sys.Path;
            var areas = S.Sys.Areas;
            var motion = cu.Agent.Motion;
            var isGrounded = motion.IsGrounded;
            var island = isGrounded ? areas.IslandAt(cu.PosIdx) : 0;
            var beingId = cu.Agent.Being.Id;
            var closest = cu.ClosestRange;
            var closestSqr = closest * closest;
            int validated = 0;
            for (int i = 0; i < n && validated < MaxValidated; i++) {
                var p = candidates[i];
                if (!path.IsNavigable(p, isGrounded)) { continue; }
                if (motion.HadNavFailureAt(p)) { continue; }
                if (S.Beings.CountAt(p) > 0) { continue; }
                // someone stands there or walks there to stop
                if (path.IsSpotTaken(p, beingId)) { continue; }
                // nobody stops on a sandbag or in a doorway
                if (path.IsPassThroughOnly(p)) { continue; }
                if (CombatUnit.IsBadPos(S, p)) { continue; }
                if (isGrounded && !areas.CanReachGroundedIslandToPos(island, p)) { continue; }
                validated++;
                var pv = Pos.ToVector(p);
                if (closest > 0f && S.Sys.Combat.FirstEnemyWithin(cu, pv, closestSqr) != null) {
                    continue;
                }
                if (!EntityUtils.HasLineOfSightCoverFast(pv, targetSightPos, 0f, rangeMax)) {
                    continue;
                }
                bestScore = -scores[i];
                return p;
            }
            return -1;
        }

        // The score of the unit's own tile, to tell whether moving is worth it. Sight and range
        // are the caller's to check.
        public static float ScoreHere(GameState S, CombatUnit cu, int targetPosIdx,
                bool useCover) {
            var attack = cu.BestAttack;
            if (attack == null || attack.IsMelee) { return 0f; }
            Pos.ToXY(targetPosIdx, out var tx, out var ty);
            return Score(S, cu, attack, cu.PosIdx, targetPosIdx, tx, ty, useCover);
        }

        private static float Score(GameState S, CombatUnit cu, AttackMethod attack, int p,
                int targetPosIdx, int tx, int ty, bool useCover) {
            float score = 1f;
            if (useCover) {
                // the cover the spot gives against the target's return fire, counted as the
                // dice count it (CoverUtils); a wall's 75% counts as full cover here
                var block = CoverUtils.BlockChance(S, p, targetPosIdx);
                score = 0.3f + 0.55f * Mathf.Min(block / CoverUtils.FullCoverBlock, 1f);
                // A wall right next to the spot, toward the target, blocks the shot as well as
                // the reply. Scored as cover, such spots filled the few that get a sight check
                // (MaxValidated) in walled rooms and no spot was found at all
                Pos.ToXY(p, out var px, out var py);
                var toward = Pos.NeighborAt(p, System.Math.Sign(tx - px), System.Math.Sign(ty - py));
                if (toward != p && S.Query.GetCoverMap.Ask()[toward] > 99) {
                    score *= 0.1f;
                }
            }
            score *= SpotMul(S, cu, p);
            var dist = Mathf.Sqrt(Pos.SqrDistance(p, targetPosIdx));
            var preferred = Mathf.Max(cu.ClosestRange, attack.PreferredDist);
            var off = Mathf.Abs(dist - preferred) / preferred;
            score *= Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(off));
            if (dist < CloseToTargetDist) {
                score *= 0.5f;
            }
            if (cu.WasRecentCover(p)) {
                // keeps a unit from bouncing between the same spots
                score *= 0.5f;
            }
            return score;
        }

        // What any spot the unit might go to is worth for where it is: less the farther it
        // walks (RW's CastPositionPreference), and less beside others (ElbowRoomMul). Cover
        // searches (CoverSys) share it.
        public static float SpotMul(GameState S, CombatUnit cu, int p) {
            var mul = Mathf.Pow(WalkDecayPerTile, Mathf.Sqrt(Pos.SqrDistance(cu.PosIdx, p)));
            var others = S.Sys.Path.OthersAround(p, cu.Agent.Being.Id);
            for (int i = 0; i < others; i++) {
                mul *= ElbowRoomMul;
            }
            return mul;
        }
    }
}
