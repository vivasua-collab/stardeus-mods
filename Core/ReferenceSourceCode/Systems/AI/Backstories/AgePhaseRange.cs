using Game.Utils;
using System;

namespace Game.Systems.Backstories {
    [Serializable]
    public struct AgePhaseRange  {
        public uint AgeFrom;
        [JsonEnum(typeof(Game.Systems.Backstories.AgePhase))]
        public string Phase;
        public AgePhase AgePhase;
    }
}
