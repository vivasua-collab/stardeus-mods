using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;
using Game.Data.Space;
using Game.CodeGen;
using Game.Systems.Ships;

using Game.Utils;
namespace Game.Components.Upgrades {
    public sealed class ThrusterUpgrade : Upgrade {
        public const string Id = "Thruster";
        public override string Icon => GraphicsId.ObjUpgradesThrusterUpgrade;
        public override string Title => T.ObjUpgradesThrusterupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.ThrusterUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new ThrusterUpgrade());
        }
        public override bool IsCompatibleWith(Entity entity) {
            if (entity.TryGetComponent<ShipEngineComp>(out var eng)) {
                if (eng.IsManeuvering) {
                    return true;
                }
                if (eng.WorksIn(UniverseLayer.Sector)) {
                    return true;
                }
            }
            return false;
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<ShipEngineComp>(out var eng)) {
                if (eng.IsManeuvering) {
                    eng.RangeUpgradeMultiplier += 0.5f;
                } else {
                    eng.PowerUpgradeMultiplier += 0.5f;
                    eng.ExtraExplosionChance += 0.025f;
                }
            }
            if (entity.TryGetComponent<ShipCombatEngineComp>(out var combEng)) {
                combEng.AddEvadeBonus(0.15f);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<ShipEngineComp>(out var eng)) {
                if (eng.IsManeuvering) {
                    eng.RangeUpgradeMultiplier -= 0.5f;
                } else {
                    eng.PowerUpgradeMultiplier -= 0.5f;
                    eng.ExtraExplosionChance -= 0.025f;
                }
            }
            if (entity.TryGetComponent<ShipCombatEngineComp>(out var combEng)) {
                combEng.AddEvadeBonus(-0.15f);
            }
        }
    }
}
