using System;
using Game.Systems.UIUX;

namespace Game.Systems.Space {
    public class HyperspaceLayerState {
        public BInt Engines = new();
        /// <summary>
        /// Ready as in running, not just charged. It involves a pilot.
        /// </summary>
        public BInt EnginesReady = new();
        /// <summary>
        /// Subset of Engines that can realistically reach a full charge (powered, reachable,
        /// fuelled). Unlike Engines, it's safe to wait for all of these to charge - a drive
        /// that's e.g. out of fuel is counted in Engines but would never reach EnginesChargeable.
        /// </summary>
        public BInt EnginesChargeable = new();
        public BInt Jumps = new();
        public BInt EnginesWarp = new();
        /// <summary>
        /// While Jumps are only to adjacent objects, warps are to any object in
        /// this layer.
        /// </summary>
        public BInt Warps = new();
        public BFloat MaxCharge = new();
    }
}
