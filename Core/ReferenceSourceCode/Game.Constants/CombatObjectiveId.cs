using Game.Utils;
using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class CombatObjectiveId {
        public const string Raid = "Raid";
        public const string Defend = "Defend";
        public const string FistFight = "FistFight";
        public const string Survive = "Survive";
        public const string Stage = "Stage";
    }
}
