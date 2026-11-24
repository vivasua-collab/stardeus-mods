using System.Linq;
using Game.Data;
using Game.Data.Factions;
using Game.Modifiers;
using Game.Systems.Naming;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Creatures {
    public static class GenBeingData {
        private static GenBeingDataStep[] Steps;
        public static bool Generate(GameState S, Rng rng, BeingData data,
                GenBeingVars vars) {
            if (Steps == null) {
                Steps = Registries.GenBeingDataSteps.All.Values.OrderBy(
                    s => s.Order).ToArray();
            }
            if (vars == null) {
                D.Err("GenBeingVars cannot be null! {0}", data);
            }
            bool isSuccess = true;
            if (vars == null) {
                vars = new GenBeingVars(data.Species, BeingType.Neutral);
                vars.Body = FactionDef.RandomBodyVars(rng);
            }
            for (int i = 0; i < Steps.Length; i++) {
                GenBeingDataStep s = Steps[i];
                if (!s.Generate(S, data, rng.Fork(), vars)) {
                    isSuccess = false;
                }
            }
            return isSuccess;
        }
    }
}
