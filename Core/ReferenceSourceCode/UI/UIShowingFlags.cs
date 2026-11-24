using System;

namespace Game.UI {
    [Flags]
    public enum UIShowingFlags : long {
        None = 0,
        QuickSearch = 1 << 0,
        MainMenu = 1 << 1,
        Dialog = 1 << 2,
        ScreenBlock = 1 << 3,
        // for UI only
        MouseTooltip = 1 << 4,
        EntityPanel = 1 << 5,
        QuickChoice = 1 << 6,
        SubPanel = 1 << 7,
        Quests = 1 << 8,
        ShipCombatMode = 1 << 9,
        Surgeries = 1 << 10,
        Tutorial = 1 << 11,
        BuildMenu = 1 << 12,
        Overlay = 1 << 13,
        ShipConfig = 1 << 14,
        FullScreenWidget = 1 << 15,
        ScrollableWidget = 1 << 16,
        Starmap = 1 << 17,
        Research = 1 << 18,
        Policies = 1 << 19,
        BeingGroup = 1 << 20,
        JobMatrix = 1 << 21,
        ImageBlowout = 1 << 22,
        LoadingScreen = 1 << 23,
        GameSetup = 1 << 24,
        PanelModal = 1 << 25,
        Terraforming = 1 << 26,
        Wallet = 1 << 27,
        Expeditions = 1 << 28,
        Codex = 1 << 29,
        Notes = 1 << 30,
        Situation = 1L << 31,
        EventLog = 1L << 32,
        Twitch = 1L << 33,
        ShipCombatMini = 1L << 34,
        Production = 1L << 35,
        TutorialPopup = 1L << 36,
        Diplomacy = 1L << 37,
        Minigame = 1L << 38,
        CombatControls = 1L << 39,
        Electricity = 1L << 40,
        // Not in AnyInGame: the form also opens on the main menu and over the loading screen
        Feedback = 1L << 41,

        //Dialog is not listed, we can't close dialog with esc, so ignore it
        AnyInGame = EntityPanel | SubPanel | Quests | FullScreenWidget |
            Starmap | Research | QuickChoice | ShipConfig | ShipCombatMode |
            BuildMenu | Overlay | QuickSearch | Surgeries | Policies |
            BeingGroup | JobMatrix | ImageBlowout | Terraforming |
            Wallet | Expeditions | Codex | Notes | Situation | EventLog | Twitch | Production |
            Diplomacy | Minigame | CombatControls | Electricity,
    }
}
