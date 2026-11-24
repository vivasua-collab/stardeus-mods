using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class SpeedUpgrade : Upgrade {
        public const string Id = "Speed";
        public override string Icon => GraphicsId.ObjUpgradesSpeedUpgrade;
        public override string Title => T.ObjUpgradesSpeedupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.SpeedUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new SpeedUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            if (entity.TryGetComponent<ISpeedUpgradeable>(out var upgr)) {
                return upgr.IsSpeedUpgradeable;
            }
            return false;
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<ISpeedUpgradeable>(
                    out var upg)) {
                upg.ApplySpeedUpgrade(0.15f);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<ISpeedUpgradeable>(
                    out var upg)) {
                upg.ApplySpeedUpgrade(-0.15f);
            }
        }
    }
}
