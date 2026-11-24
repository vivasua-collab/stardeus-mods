using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Utils;

namespace Game.Components.Upgrades {
    // Lets a Shield Emitter raise the shield against a magnetic storm, and while it is up the
    // storm stops disrupting the ship's grid - see
    // Docs/Architecture/Systems/MagneticStormShielding.md. Deliberately no OnInstall side
    // effect: presence is read from the slots on demand via ShipShieldComp.HasEMPShield.
    public sealed class EMPShieldUpgrade : Upgrade {
        public const string Id = "EMPShield";
        public override string Icon => GraphicsId.ObjUpgradesEMPShieldUpgrade;
        public override string Title => T.ObjUpgradesEmpshieldupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.EMPShieldUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new EMPShieldUpgrade());
        }

        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<ShipShieldComp>();
        }
    }
}
