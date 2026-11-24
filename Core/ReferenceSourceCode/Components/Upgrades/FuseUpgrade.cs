using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class FuseUpgrade : Upgrade {
        public const string Id = "Fuse";
        public override string Icon => GraphicsId.ObjUpgradesFuseUpgrade;
        public override string Title => T.ObjUpgradesFuseupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.FuseUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new FuseUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<WithFuseComp>();
        }

        public override void OnInstall(Entity entity) {
            // with fuse component will check if fuse is available when necessary
        }

        public override void OnUninstall(Entity entity) {
        }
    }
}
