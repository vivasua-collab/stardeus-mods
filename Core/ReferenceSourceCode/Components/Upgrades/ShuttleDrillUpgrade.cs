using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class ShuttleDrillUpgrade : Upgrade {
        public const string Id = "ShuttleDrill";
        public override string Icon => GraphicsId.ObjUpgradesShuttleDrillUpgrade;
        public override string Title => T.ObjUpgradesShuttledrillupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.ShuttleDrillUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new ShuttleDrillUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<ShuttleComp>();
        }

        public override bool CanInstallNow(Entity entity) {
            if (entity.TryGetComponent<ShuttleComp>(out var sh)) {
                return !sh.IsOut;
            }
            return false;
        }

        public override void OnInstall(Entity entity) {
            if (entity is Tile t && t.TryGetComponent<ShuttleComp>(out var shuttle)) {
                shuttle.AddMiningPower(1);
                shuttle.AddMiningDepth(1f);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity is Tile t && t.TryGetComponent<ShuttleComp>(out var shuttle)) {
                shuttle.AddMiningPower(-1);
                shuttle.AddMiningDepth(-1f);
            }
        }
    }
}
