using Game.CodeGen;
using Game.Data;
using Game.Systems.Ships;
using Game.Utils;
using KL.Utils;

namespace Game.Components.Upgrades {
    public sealed class WeaponCapacityUpgrade : Upgrade {
        public const string Id = "WeaponCapacity";
        public const int CapacityPerUpgrade = 1;
        public override string Icon => "Obj/Upgrades/WeaponCapacityUpgrade";
        public override string Title => "Weapon Capacity";
        public override string UpgradeId => Id;
        public override string MatTypeId => "WeaponCapacityUpgrade";

        [SelfInit]
        private static void Register() {
            Add(Id, new WeaponCapacityUpgrade());
        }

        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<BattleStationComp>();
        }

        public override void OnInstall(Entity entity) {
            if (entity.TryGetComponent<BattleStationComp>(out var bs)) {
                bs.ExtraWeaponCapacity += CapacityPerUpgrade;
                RecalculateIfInCombat(bs);
            }
        }

        public override void OnUninstall(Entity entity) {
            if (entity.TryGetComponent<BattleStationComp>(out var bs)) {
                bs.ExtraWeaponCapacity -= CapacityPerUpgrade;
                RecalculateIfInCombat(bs);
            }
        }

        private static void RecalculateIfInCombat(BattleStationComp bs) {
            var playerShip = A.Player?.Ship;
            if (playerShip?.IsInCombat == true) {
                ShipCombatAI.RecalculateBattleStationAssignments(playerShip.CombatState);
            }
        }
    }
}
