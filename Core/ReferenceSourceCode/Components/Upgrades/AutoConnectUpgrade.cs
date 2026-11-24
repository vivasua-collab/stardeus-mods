using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;
using Game.CodeGen;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class AutoConnectUpgrade : Upgrade {
        public const string Id = "AutoConnect";
        public override string Icon => GraphicsId.ObjUpgradesAutoConnectUpgrade;
        public override string Title => T.ObjUpgradesAutoconnectupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.AutoConnectUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new AutoConnectUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<ShipComputerComp>();
        }

        public override void OnInstall(Entity entity) {
            var countAfter = entity.S.BB.SyncAdd(BBKeyH.AutoConnectUpgradeCount, 1);
            // D.Log("Auto connect upgrade count: {0}", countAfter);
        }

        public override void OnUninstall(Entity entity) {
            var countAfter = entity.S.BB.SyncAdd(BBKeyH.AutoConnectUpgradeCount, -1);
            if (countAfter < 0) {
                D.Err("Auto connect upgrade count is < 0! {0}. Setting to 0",
                    countAfter);
                entity.S.BB.Set(BBKeyH.AutoConnectUpgradeCount, 0);
            }
        }
    }
}
