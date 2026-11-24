using System;

namespace Game.Systems.Traits {
    [Serializable]
    public struct TraitWithChance {
        public TraitDef Trait;
        public string TraitId;
        public float Chance;
    }

}
