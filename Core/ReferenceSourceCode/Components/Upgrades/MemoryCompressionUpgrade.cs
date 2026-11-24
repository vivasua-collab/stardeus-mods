using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class MemoryCompressionUpgrade : Upgrade {
        public const string Id = "MemoryCompression";
        public override string Icon => GraphicsId.ObjUpgradesMemoryCompressionUpgrade;
        public override string Title => T.ObjUpgradesMemorycompressionupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.MemoryCompressionUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new MemoryCompressionUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<MemoryModuleComp>();
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<MemoryModuleComp>(out var mem)) {
                mem.AddCompression(0.5f);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<MemoryModuleComp>(out var mem)) {
                mem.AddCompression(-0.5f);
            }
        }
    }
}
