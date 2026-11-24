using Game.Systems.Path;

namespace Game.Systems.Reg {
    // Region hop counts from one position to many targets (RegSys.RegionDists).
    // Penalties and Jitter describe what the caller adds to a hop count when it ranks the
    // targets, so the search can stop once nothing it has not reached yet could still win.
    public struct RegDistsReq {
        public int FromPos;
        public bool Grounded;
        public NavPermission Permissions;
        public int[] Targets;
        public int Count;
        // Per target, added to its hop count by the caller. Null = no early stop
        public float[] Penalties;
        // Largest random tie breaker the caller adds on top of Penalties
        public float Jitter;
        // Targets further than this many hops count as unreachable, -1 = no limit
        public int MaxDist;
    }
}
