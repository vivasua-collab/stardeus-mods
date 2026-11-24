using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class EnergyEfficiencyUpgrade : Upgrade {
        public const string Id = "EnergyEfficiency";
        public override string Icon => GraphicsId.ObjUpgradesEnergyEfficiencyUpgrade;
        public override string Title => T.ObjUpgradesEnergyefficiencyupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.EnergyEfficiencyUpgrade ;
        public const float UpgradeAmount = 0.15f;

        [SelfInit]
        private static void Register() {
            Add(Id, new EnergyEfficiencyUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            if (entity.TryGetComponent<IEnergyEfficiencyUpgradeable>(out var upgr)) {
                return upgr.IsEnergyEfficiencyUpgradeable;
            }
            return false;
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<IEnergyEfficiencyUpgradeable>(
                    out var processor)) {
                processor.ApplyEfficiencyUpgrade(UpgradeAmount);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<IEnergyEfficiencyUpgradeable>(
                    out var processor)) {
                processor.ApplyEfficiencyUpgrade(-UpgradeAmount);
            }
        }
    }
}
