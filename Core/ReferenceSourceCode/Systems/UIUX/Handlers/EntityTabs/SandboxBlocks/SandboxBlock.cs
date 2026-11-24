using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;

namespace Game.Systems.UIUX {
    public abstract class SandboxBlock {
        public abstract string Id { get; }
        public abstract int Order { get; }

        protected static void Register(SandboxBlock block) {
            Registries.SandboxBlocks.Add(block.Id, block);
        }

        public virtual bool ShowFor(GameState s, BeingData being) => false;
        public virtual bool ShowFor(GameState s, Tile tile) => false;
        public virtual bool ShowFor(GameState s, ObjData obj) => false;

        public abstract void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx);
    }
}
