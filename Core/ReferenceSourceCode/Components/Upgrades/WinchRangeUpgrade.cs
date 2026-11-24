using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class WinchRangeUpgrade : Upgrade {
        public const string Id = "WinchRange";
        public override string Icon => GraphicsId.ObjUpgradesWinchRangeUpgrade;
        public override string Title => T.ObjUpgradesWinchrangeupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.WinchRangeUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new WinchRangeUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<WinchHookComp>();
        }

        public override bool CanInstallNow(Entity entity) {
            if (entity.TryGetComponent<WinchHookComp>(out var hook)) {
                return !hook.IsRangeUpgraded;
            }
            return false;
        }

        public override void OnInstall(Entity entity) {
            if (entity is Tile t && t.TryGetComponent<WinchHookComp>(out var hook)) {
                hook.InstallRangeUpgrade(true);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity is Tile t && t.TryGetComponent<WinchHookComp>(out var hook)) {
                hook.InstallRangeUpgrade(false);
            }
        }
    }
}
