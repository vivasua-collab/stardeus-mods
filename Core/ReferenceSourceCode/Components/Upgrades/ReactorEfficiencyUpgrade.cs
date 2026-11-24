using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class ReactorEfficiencyUpgrade : Upgrade {
        public const string Id = "ReactorEfficiency";
        public override string Icon => GraphicsId.ObjUpgradesReactorEfficiencyUpgrade;
        public override string Title => T.ObjUpgradesReactorefficiencyupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.ReactorEfficiencyUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new ReactorEfficiencyUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<MatterReactorComp>();
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<MatterReactorComp>(out var reactor)) {
                reactor.UpgradeEfficiency += 0.33f;
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<MatterReactorComp>(out var reactor)) {
                reactor.UpgradeEfficiency -= 0.33f;
            }
        }
    }
}
