using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestProviderType : int {
        None = 0,
        Ship = 1 << 0,
        ShipDerelict = 1 << 1,
        Signal = 1 << 2,
        ShipComputer = 1 << 3,
        Colonist = 1 << 4,
        SpaceStation = 1 << 5,
        POI = 1 << 6,
        Anomaly = 1 << 7,
        Port = 1 << 8,
        SpaceObject = 1 << 9,
    }
}
