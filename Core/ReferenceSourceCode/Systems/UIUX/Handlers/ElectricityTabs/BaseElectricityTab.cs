using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public abstract class BaseElectricityTab {
        public abstract string Name { get; }

        public abstract void LoadContent(Lifetime parentLifetime, GameState s,
            WGroup container, int gridIdx, ElectricityWidget widget);
    }
}
