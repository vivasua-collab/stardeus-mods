using Game.Utils;
using System;

namespace Game.Systems.BodyParts {
    [Serializable]
    public struct DamageResistance {
        [JsonEnum(typeof(Game.Constants.DamageType))]
        public string DamageType;
        public float Resistance;
    }
}
