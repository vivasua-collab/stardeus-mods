using System.Collections.Generic;
using Game.Components;
using Game.CodeGen;
using Game.Utils;

namespace Game.Systems.AI {
    public sealed class NeedsComp : BaseComponent<NeedsComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new NeedsComp());
        }

        protected override void OnConfig() { }

        public readonly HashSet<int> ShowingNeedInfo = new();

        public override string ToString() {
            return " * Being Needs";
        }
    }
}
