using Game.Scenarios;
using Game.Systems.UIUX;

namespace Game.Procgen {
    public interface IWithScenarioConfig {
        void SetConfig(GameSetupData config);
    }
}
