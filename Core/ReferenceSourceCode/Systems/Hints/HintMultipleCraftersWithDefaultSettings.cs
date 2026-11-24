using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.Crafting;

namespace Game.Systems.Hints {
    public class HintMultipleCraftersWithDefaultSettings : Hint {
        public const string Id = "multi_crafters_with_defaults";
        protected override string id => Id;
        protected override bool IsRecurring => true;
        public override string Text => T.HintMulti_Crafters_With_Defaults_V1.F(
            CraftingDemand.DefaultAmount);

        [SelfInit]
        static void AutoRegister() {
            Register(new HintMultipleCraftersWithDefaultSettings());
        }
    }
}
