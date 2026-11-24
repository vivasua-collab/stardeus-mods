using System;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;

namespace Game.Systems.UIUX {
    public abstract class EntityOverviewBlock {
        public abstract int Order { get; }
        public abstract string Id { get; }
        // Same opt-in as BaseEntityTab.ShowForUnrevealed, for the blocks of the overview
        public virtual bool ShowForUnrevealed => false;
        protected static void Register(EntityOverviewBlock block) {
            Registries.EntityOverviewBlocks.Add(block.Id, block);
        }

        public virtual bool ShowFor(GameState s, BeingData being) { return false; }
        public virtual bool ShowFor(GameState s, Tile tile) { return false;}
        public virtual bool ShowFor(GameState s, ObjData obj) { return false;}

        public abstract void LoadContent(EntityOverviewContext ctx, WGroup group, ref int gIdx);
        public virtual void LoadFooter(EntityOverviewContext ctx, WGroup group, ref int gIdx) { }
    }
}
