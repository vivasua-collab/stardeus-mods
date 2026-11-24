using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class ShuttleStorageUpgrade : Upgrade {
        public const string Id = "ShuttleStorage";
        public override string Icon => GraphicsId.ObjUpgradesShuttleStorageUpgrade;
        public override string Title => T.ObjUpgradesShuttlestorageupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.ShuttleStorageUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new ShuttleStorageUpgrade());
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
            if (entity is Tile t) {
                t.MaterialStorage.ExtraCapacity += 100;
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity is Tile t) {
                t.MaterialStorage.ExtraCapacity -= 100;
                if (t.MaterialStorage.ExtraCapacity < 0) {
                    D.Err("Uninstalled an upgrade resulted in <0 capacity! {0} {1}",
                        this, entity);
                    t.MaterialStorage.ExtraCapacity = 0;
                }
            }
        }
    }
}
