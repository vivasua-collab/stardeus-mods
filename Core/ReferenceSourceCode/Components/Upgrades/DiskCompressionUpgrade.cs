using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class DiskCompressionUpgrade : Upgrade {
        public const string Id = "DiskCompression";
        public override string Icon => GraphicsId.ObjUpgradesDiskCompressionUpgrade;
        public override string Title => T.ObjUpgradesDiskcompressionupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.DiskCompressionUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new DiskCompressionUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<DiskModuleComp>();
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<DiskModuleComp>(out var disk)) {
                disk.AddCompression(0.5f);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<DiskModuleComp>(out var disk)) {
                disk.AddCompression(-0.5f);
            }
        }
    }
}
