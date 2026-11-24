using System.Collections.Generic;
using Game.Scenarios;
using Game.Systems.UIUX;

namespace Game.Procgen {
    // A scenario's starting stasis array, filled with the people rolled on the Custom Start
    // screen when there are any. A subclass rather than config on the base stage, because
    // ShipFactory.RunStages (game clones) refuses stages that take a scenario config, and
    // derelicts and clones should keep rolling their own sleepers.
    public sealed class ScenarioStasisAreaGenStage : StasisAreaGenStage, IWithScenarioConfig {
        private GameSetupData config;

        public ScenarioStasisAreaGenStage(
                StasisManifest manifest,
                int minDistToCPU,
                int minDistToEngine,
                int addDeadBodies = 0,
                float minIntegrity = 0.85f,
                bool doSomeDamageToTheArea = false)
            : base(manifest, minDistToCPU, minDistToEngine, addDeadBodies, minIntegrity,
                doSomeDamageToTheArea) {
        }

        public void SetConfig(GameSetupData config) {
            this.config = config;
        }

        protected override List<StartingBeingData> PresetSleepers() {
            if (config == null) { return null; }
            return config.StasisSleepers.TryGetValue(manifest, out var sleepers)
                ? sleepers
                : null;
        }
    }
}
