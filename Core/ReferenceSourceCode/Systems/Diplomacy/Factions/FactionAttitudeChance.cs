using System;
using KL.Utils;

namespace Game.Systems.Diplomacy {
    [Serializable]
    public class FactionAttitudeChance {
        public string FactionId;
        public float[] Chance;
        private Curve curve;
        public Curve Curve => curve ??= Chance;

        public override string ToString() {
            return $"FactionAttitudeChance[{FactionId}->{Curve}]";
        }
    }
}
