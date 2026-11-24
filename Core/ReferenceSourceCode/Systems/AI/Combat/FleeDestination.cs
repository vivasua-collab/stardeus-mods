using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.Systems.Reg;
using KL.Grid;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    // Where a fleeing unit runs to. Bots never look for cover (CombatUnit.IsTactical), and
    // humans often find none, so without a destination of its own a flee was a random roam
    // that went toward the enemy as often as away from it (STA-1713)
    public static class FleeDestination {
        // Regions are at most a map chunk across, so this reaches about 40 tiles away
        private const int SearchRegions = 6;
        // Past this many tiles beyond the flee radius, being farther no longer counts and a
        // shorter run wins
        private const float ExtraDistance = 4f;

        private static readonly System.Func<int, bool> checkAway = CheckAway;
        private static readonly System.Func<int, bool> checkHome = CheckHome;
        private static AIAgentComp agent;
        private static BeingSafety safety;
        private static PathSys path;
        private static AreaSys areas;
        private static bool isGrounded;
        private static bool isSafeNow;
        private static bool isSurvivableNow;
        private static int fromPos;
        private static int threatPos;
        private static int fromThreatSqr;
        private static float minThreatSqr;
        private static float capThreatSqr;
        private static int shipIsland;
        private static int best;
        private static float bestScore;
        private static int bestTravelSqr;

        public static int Find(GameState S, AIAgentComp forAgent, int threatPosIdx) {
            if (forAgent.PosIdx < 1 || threatPosIdx < 1) { return Pos.Invalid; }
            Begin(S, forAgent, threatPosIdx);
            try {
                // the crew runs home to the ship from a derelict or from space
                if (forAgent.BeingData.EffectiveFaction.IsPlayer) {
                    var home = FindHome(S);
                    if (home != Pos.Invalid && CanReach(S, home)) { return home; }
                }
                return FindAway(S);
            } finally {
                agent = null;
                safety = null;
                path = null;
                areas = null;
            }
        }

        private static void Begin(GameState S, AIAgentComp forAgent, int threatPosIdx) {
            agent = forAgent;
            safety = forAgent.BeingData.Safety;
            path = S.Sys.Path;
            areas = S.Sys.Areas;
            isGrounded = forAgent.Motion.IsGrounded;
            fromPos = forAgent.PosIdx;
            threatPos = threatPosIdx;
            isSafeNow = safety.IsActuallySafeAt(fromPos);
            isSurvivableNow = isSafeNow || safety.IsActuallySurvivableAt(fromPos);
            fromThreatSqr = Pos.SqrDistance(fromPos, threatPos);
            var r = Tunables.CombatFleeRadius;
            minThreatSqr = r * r;
            capThreatSqr = (r + ExtraDistance) * (r + ExtraDistance);
            best = Pos.Invalid;
            bestScore = float.MinValue;
            bestTravelSqr = int.MaxValue;
        }

        private static int FindHome(GameState S) {
            if (!areas.TryGetShipIsland(out shipIsland)) { return Pos.Invalid; }
            if (areas.IslandAt(fromPos) == shipIsland) { return Pos.Invalid; }
            var cpu = ShipComputerComp.FindMain(S);
            if (cpu == null) { return Pos.Invalid; }
            // the computer blocks its own tiles, and a region search can't start inside it
            var tile = cpu.Tile;
            var start = Pos.Invalid;
            Pos.ForEachInRadius(tile.CenterIdx, tile.Size.magnitude + 1f, false, (p) => {
                if (path.IsPassable(p)) {
                    start = p;
                    return false;
                }
                return true;
            });
            if (start == Pos.Invalid) { return Pos.Invalid; }
            return S.Query.FindNearby.Ask(start, isGrounded, 3, checkHome);
        }

        // Walking crew on a derelict with no bridge can't get home, and every home tile would
        // fail its path request in turn
        private static bool CanReach(GameState S, int to) {
            var req = RegPathReq.Create(fromPos, to, isGrounded, true);
            req.Permissions = agent.Motion.Permissions;
            return S.Query.RegionDist.Ask(req) != -1;
        }

        private static int FindAway(GameState S) {
            S.Query.FindNearby.Ask(fromPos, isGrounded, SearchRegions, checkAway);
            return best;
        }

        private static bool CheckHome(int p) {
            return IsUsable(p) && areas.IslandAt(p) == shipIsland
                && Pos.SqrDistance(p, threatPos) > minThreatSqr;
        }

        // Scores every candidate and never stops the search early: the farthest spot from
        // the threat (up to the cap) wins, then the shortest run
        private static bool CheckAway(int p) {
            if (!IsUsable(p)) { return false; }
            var threatSqr = Pos.SqrDistance(p, threatPos);
            if (threatSqr <= fromThreatSqr) { return false; }
            var score = Mathf.Min(threatSqr, capThreatSqr);
            var travelSqr = Pos.SqrDistance(fromPos, p);
            if (score > bestScore || (score == bestScore && travelSqr < bestTravelSqr)) {
                best = p;
                bestScore = score;
                bestTravelSqr = travelSqr;
            }
            return false;
        }

        private static bool IsUsable(int p) {
            if (p < 1 || agent.Motion.HadNavFailureAt(p)) { return false; }
            if (!path.IsNavigable(p, isGrounded)) { return false; }
            if (isSafeNow) { return safety.IsActuallySafeAt(p); }
            if (isSurvivableNow) { return safety.IsActuallySurvivableAt(p); }
            return true;
        }
    }
}
