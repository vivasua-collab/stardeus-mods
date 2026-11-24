
using Game.Data;
using Game.Systems.UIUX;

namespace Game.Components {
    public interface IWildPlant {
        public Obj Obj { get; }
        public void Grow(float growthAmount, int ticksPassed);
        public float Growth { get; }
        public bool IsGrowing { get; }
        public string GrowthState { get; }
        public BFloat GrowthB { get; }
        public BCallback StateChangedB { get; }
    }
}
