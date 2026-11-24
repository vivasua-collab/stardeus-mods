using Game.Data;

namespace Game.Systems.UIUX {
    public abstract class ResearchMenuBlock {
        protected readonly ResearchMenuWidget parent;
        protected readonly GameState S;
        protected readonly ResearchContext ctx;
        public abstract int Order { get; }

        protected ResearchMenuBlock(ResearchMenuWidget parent, ResearchContext ctx, GameState s) {
            this.parent = parent;
            this.ctx = ctx;
            S = s;
        }

        public abstract void Load(int siblingIdx);

        // Repaints in place after Load(); default no-op for blocks with nothing dynamic.
        public virtual void Refresh() { }
    }
}
