using Game.Data;

namespace Game.Systems.UIUX {
    public abstract class StarmapMenuBlock {
        protected readonly StarmapMenuWidget parent;
        protected readonly GameState S;
        protected readonly StarmapContext ctx;
        public abstract int Order { get; }

        protected StarmapMenuBlock(StarmapMenuWidget parent, StarmapContext ctx, GameState s) {
            this.parent = parent;
            this.ctx = ctx;
            S = s;
        }

        public abstract void Load(int siblingIdx);
    }
}
