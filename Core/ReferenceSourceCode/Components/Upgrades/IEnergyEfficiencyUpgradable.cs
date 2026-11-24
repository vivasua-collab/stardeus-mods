namespace Game.Components.Upgrades {
    public interface IEnergyEfficiencyUpgradeable {
        void ApplyEfficiencyUpgrade(float amt);
        bool IsEnergyEfficiencyUpgradeable { get; }
    }
}
