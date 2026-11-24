using Game.Components.Upgrades;

namespace Game.Systems.UIUX {
    public abstract class ShipComputerApp_Minigame : ShipComputerApp {
        public override bool IsVisible => GameCartridgeUpgrade.IsUnlocked(Id);
    }
}
