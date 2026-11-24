using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class InventorySlotId {
        // space suit, armor, etc
        public const string Suit = "Suit";
        // space helmet, armor helmet
        public const string Helmet = "Helmet";
        // shirt + pants, onesie
        public const string Clothing = "Clothing";
        // bandana, hat
        public const string Hat = "Hat";
        public const string Weapon = "Weapon";
    }
}
