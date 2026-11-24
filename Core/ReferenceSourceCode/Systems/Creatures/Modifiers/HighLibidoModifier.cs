using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public class HighLibidoModifier : BeingModifier {
        public override string Id => "HighLibido";
        public override string Tooltip => T.TraitHighLibido;
        [SelfInit]
        private static void Register() {
            RegisterModifier(new HighLibidoModifier());
        }

        public override void AfterGenerateBeing(GameState s, BeingData data,
                Rng rng, GenBeingVars vars) {
            data.Body.MaxLibido += rng.Range(0.4f, 0.6f);
        }
    }
}
