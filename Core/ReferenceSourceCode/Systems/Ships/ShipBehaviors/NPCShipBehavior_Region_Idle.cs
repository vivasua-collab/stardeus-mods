using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipBehavior_Region_Idle : NPCShipBehavior {
        public const string BehaviorId = "Region_Idle";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Region_Idle());
        }
        public override string Id => BehaviorId;
        public override bool CanHyperjump => true;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Region;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override void DoTurn(NPCShip ship) {
            if (ship.IsInHyperspace) {
                var newBeh = ship.ChangeBehavior(NPCShipBehaviorScope.Region, false);
                if (newBeh == this) {
                    ship.SetWantsHyperspace(false);
                }
            }
        }
        public static void AssignTo(NPCShip ship) {
            ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                NPCShipBehaviorScope.Region);
        }

    }
}
