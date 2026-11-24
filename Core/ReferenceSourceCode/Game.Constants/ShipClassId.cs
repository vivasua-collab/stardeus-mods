using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ShipClassId {
        public const string Unclassified = nameof(Unclassified);
        public const string SpaceStation = nameof(SpaceStation);
        public const string Fighter = nameof(Fighter);
        public const string Frigate = nameof(Frigate);
        public const string Corvette = nameof(Corvette);
        public const string Carrier = nameof(Carrier);
        public const string Cruiser = nameof(Cruiser);
        public const string Battlecruiser = nameof(Battlecruiser);
        public const string Battleship = nameof(Battleship);
        public const string Titan = nameof(Titan);
        public const string Dreadnought = nameof(Dreadnought);
        public const string Destroyer = nameof(Destroyer);
    }
}
