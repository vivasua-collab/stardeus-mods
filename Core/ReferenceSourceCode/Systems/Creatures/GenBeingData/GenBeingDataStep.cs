using Game.Data;
using KL.Randomness;

namespace Game.Systems.Creatures {
    public abstract class GenBeingDataStep {
        public static void RegisterStep(GenBeingDataStep step) {
            Registries.GenBeingDataSteps.Add(step.Id, step);
        }
        public abstract string Id { get; }
        /// <summary>
        /// Lower number goes first
        /// </summary>
        public abstract int Order { get; }
        /// <returns>false if generation has failed</returns>
        public abstract bool Generate(GameState S, BeingData data, Rng rng,
            GenBeingVars vars);
    }
}
