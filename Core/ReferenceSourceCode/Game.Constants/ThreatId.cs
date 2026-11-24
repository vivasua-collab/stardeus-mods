
using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ThreatId {
        public const string Asteroid = "Asteroid";
        public const string Meteor = "Meteor";
        public const string ShieldableSpaceThreat = "ShieldableSpaceThreat";
        public const string Being = "Being";
        public const string Fire = "Fire";
        public const string BreachCapsule = "BreachCapsule";
    }
}
