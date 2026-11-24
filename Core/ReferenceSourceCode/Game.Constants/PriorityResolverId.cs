using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class PriorityResolverId {
        public const string PrepareForAttack = "PrepareForAttack";
        public const string Advance = "Advance";
        public const string AttackTurret = "AttackTurret";
        public const string SearchForEnemy = "SearchForEnemy";
        public const string GetInCover = "GetInCover";
        public const string HoldPosition = "HoldPosition";
        public const string AttackShipComputer = "AttackShipComputer";
        public const string Flee = "Flee";
        public const string BreakUpFriendlyFight = "BreakFriendlyFight";
        public const string Respond = "Respond";
    }
}
