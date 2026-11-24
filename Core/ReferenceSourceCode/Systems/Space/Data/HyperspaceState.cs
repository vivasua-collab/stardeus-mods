namespace Game.Systems.Space {
    public class HyperspaceState {
        public bool IsInHyperspace => (Flags & HyperspaceStateFlags.InHyperspace) == HyperspaceStateFlags.InHyperspace;
        public bool WantsHyperspace => (Flags & HyperspaceStateFlags.WantsHyperspace) == HyperspaceStateFlags.WantsHyperspace;
        public bool CanEnterHyperspace => (Flags & HyperspaceStateFlags.CanEnterHyperspace) == HyperspaceStateFlags.CanEnterHyperspace;
        public bool IsPreparing => (Flags & HyperspaceStateFlags.PreparingForJump) == HyperspaceStateFlags.PreparingForJump;
        public HyperspaceStateFlags Flags;
        public HyperspaceLayerState Region = new();
        public HyperspaceLayerState Universe = new();
    }
}
