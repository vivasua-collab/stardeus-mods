namespace Game.Constants {
    // Maintain order, this enum is used in unity prefab, if order changes,
    // it will break
    public enum OverlayType : byte {
        Visual = 0, // Oxygen, Heat, Rooms
        Information = 1, // Resources, Beings, Quests, Goals, etc
        Special = 2, // starmap, research, codex
        Controls = 3, // terraforming, security, combat, expeditions
        Hidden = 4, // Not adding an icon
    }
}
