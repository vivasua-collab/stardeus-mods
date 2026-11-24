using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Reg;
using KL.Grid;

namespace Game.Systems.AI.Combat {
    // How a losing raider leaves: off the map edge, the survivors running off. A flyer always can. A grounded raider only when it can walk to an edge,
    // which aboard a ship in space it almost never can; then it fights on until it is down or
    // dead. Vanishing into the capsule it came in by read as a teleport and was dropped.
    public static class EscapeRoute {
        // the walk to an edge is region pathing, so a fleeing unit, which asks every tick,
        // keeps the answer this long
        private const long RecheckTicks = Consts.TicksPerMinute * 10;

        public static bool Exists(CombatUnit cu) {
            var agent = cu.Agent;
            if (agent.Motion.IsFlying) { return true; }
            var ticks = agent.S.Ticks;
            if (cu.EdgeReachCheckTick == 0 || ticks - cu.EdgeReachCheckTick >= RecheckTicks) {
                cu.EdgeReachCheckTick = ticks;
                cu.CanWalkToMapEdge = CanReachAnyMapEdge(agent.S, agent);
            }
            return cu.CanWalkToMapEdge;
        }

        // The flee goal a unit gets: the escape goal when it has a way out, local flight
        // otherwise
        public static int FleeVarFor(CombatUnit cu) {
            return Exists(cu) ? AIVarsH.HasEscapedMap : AIVarsH.HasFled;
        }

        public static bool CanReachAnyMapEdge(GameState S, AIAgentComp agent) {
            var isG = agent.Motion.IsGrounded;
            int from = agent.PosIdx;
            Pos.ToXY(from, out int x, out int y);
            var perms = agent.Motion.Permissions;
            // Nearest border tile in each cardinal direction. A walled-in unit's
            // region connects to none of them.
            return IsRegionReachable(S, from, Pos.FromXY(0, y), isG, perms)
                || IsRegionReachable(S, from, Pos.FromXY(Pos.GridW - 1, y), isG, perms)
                || IsRegionReachable(S, from, Pos.FromXY(x, 0), isG, perms)
                || IsRegionReachable(S, from, Pos.FromXY(x, Pos.GridH - 1), isG, perms);
        }

        private static bool IsRegionReachable(GameState S, int from, int to, bool isG,
                NavPermission permissions) {
            // near:true so a border tile that is itself a wall still resolves via an
            // adjacent navigable tile.
            var req = RegPathReq.Create(from, to, isG, true);
            req.Permissions = permissions;
            return S.Query.RegionDist.Ask(req) != -1;
        }
    }
}
