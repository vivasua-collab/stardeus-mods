namespace Game.Components.Upgrades {
    public interface ISpeedUpgradeable {
        void ApplySpeedUpgrade(float amt);
        bool IsSpeedUpgradeable { get; }
    }
}
