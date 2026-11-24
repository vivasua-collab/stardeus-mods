using Game.Data;
using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingLooksDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingLooksDataStep());
        }
        private const string StepId = "Looks";
        public override string Id => StepId;
        public override int Order => 10;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            The.Looks.Generate(rng, data, data.Species.LooksDef, vars);
            data.Looks.BodyType = vars.BodyType;
            return true;
        }
    }
}
