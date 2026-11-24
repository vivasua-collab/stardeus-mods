using Game.Data;
using KL.Randomness;

namespace Game.Systems.Creatures {
    public abstract class BeingModifier : IResettable {
        public static void RegisterModifier(BeingModifier mod) {
            Registries.BeingModifiers.Add(mod.Id, mod);
        }
        protected virtual void OnCleanup() {}
        public abstract string Id { get; }
        public abstract string Tooltip { get; }
        public virtual void AfterGenerateBeing(GameState s, BeingData data, Rng rng, GenBeingVars vars) {}
        public virtual void AfterLoadBeing(GameState s, BeingData data, bool isLoading) {}

        public void OnReset() {
            OnCleanup();
        }
    }
}
