using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class AutoPilotUpgrade : Upgrade {
        public const string Id = "AutoPilot";
        public override string Icon => GraphicsId.ObjUpgradesAutoPilotUpgrade;
        public override string Title => T.ObjUpgradesAutopilotupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.AutoPilotUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new AutoPilotUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            if (entity.HasComponent<BridgeControlsComp>()) {
                return true;
            }
            if (entity.HasComponent<ShuttleComp>()) {
                return true;
            }
            return false;
        }

        public override bool CanInstallNow(Entity entity) {
            if (entity.TryGetComponent<ShuttleComp>(out var sh)) {
                return !sh.IsOut;
            }
            return base.CanInstallNow(entity);
        }

        public override void OnInstall(Entity entity) {
            if (entity is Tile t && t.TryGetComponent<BridgeControlsComp>(out var bridge)) {
                bridge.IsOperatorRequired = false;
                if (t.TryGetComponent<OperatableDeviceComp>(out var op)) {
                    op.IsAutomated = true;
                    op.DismissOperator(true);
                }
                if (t.TryGetComponent<SafetyCheckComp>(out var safe)) {
                    safe.SetIgnored(true);
                }
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity is Tile t && t.TryGetComponent<BridgeControlsComp>(out var bridge)) {
                bridge.IsOperatorRequired = true;
                if (t.TryGetComponent<OperatableDeviceComp>(out var op)) {
                    op.IsAutomated = false;
                }
                if (t.TryGetComponent<SafetyCheckComp>(out var safe)) {
                    safe.SetIgnored(false);
                }
            }
        }
    }
}
