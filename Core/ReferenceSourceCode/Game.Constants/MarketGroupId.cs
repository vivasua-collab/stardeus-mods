using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class MarketGroupId {
        public const string Ore = nameof(Ore);
        public const string Organic = nameof(Organic);
        public const string Food = nameof(Food);
        public const string Flora = nameof(Flora);
        public const string BlackMarket = nameof(BlackMarket);
        public const string Material = nameof(Material);
        public const string Upgrade = nameof(Upgrade);
        public const string RareUpgrade = nameof(RareUpgrade);
        public const string BodyPart = nameof(BodyPart);
    }
}
