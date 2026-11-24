using System;
using Game.Components;
using Game.Data;

namespace Game.Systems.UIUX {
    public abstract class ExpeditionMenuBlock {
        protected readonly ExpeditionsWidget parent;
        protected readonly GameState S;
        protected ExpeditionContext ctx => parent.Ctx;
        public abstract int Order { get; }

        protected ExpeditionMenuBlock(ExpeditionsWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public abstract void Load(int idx, ShuttleComp shuttle, Expedition exp);
    }
}
