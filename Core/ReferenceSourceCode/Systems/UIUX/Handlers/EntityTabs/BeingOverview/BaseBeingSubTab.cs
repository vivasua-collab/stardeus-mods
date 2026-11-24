using System.Net.Configuration;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public abstract class BaseBeingSubTab {
        protected BeingData being => ctx.Being;
        protected GameState S => A.S;
        protected readonly EntityOverviewContext ctx;
        protected Lifetime lifetime;
        public BaseBeingSubTab(EntityOverviewContext ctx) {
            lifetime = ctx.Lifetime.CreateChild($"BeingSubTab_{GetType().Name}");
            this.ctx = ctx;
        }

        public abstract bool Spawn(RectTransform container, int siblingIdx);

        public virtual void Cleanup() { }
    }
}
