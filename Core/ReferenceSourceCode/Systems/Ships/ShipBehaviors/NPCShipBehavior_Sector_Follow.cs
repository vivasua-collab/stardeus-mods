using System.Data.SqlClient;
using Game.Data;
using Game.Data.Space;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    /// <summary>
    /// This behavior doesn't have assignment def, because it will be assigned
    /// manually from code when needed
    /// </summary>
    public sealed class NPCShipBehavior_Sector_Follow : NPCShipBehavior {
        public const string BehaviorId = "Sector_Follow";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_Follow());
        }
        public override string Id => BehaviorId;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        public override bool CanHyperjump => false;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(true);
        }

        public static void AssignTo(NPCShip ship, SpaceObject target) {
            if (ship.IsDerelict) {
                D.Err("Trying to assign SectorFollow behavior to a derelict: {0} (target: {1})",
                    ship, target);

                return;
            }
            if (ship.NumThrustersActual > 0) {
                ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                    NPCShipBehaviorScope.Sector);
                ship.Memory.SetFollowTarget(target);
            } else {
                D.Err("Trying to assign Follow behavior to ship without thrusters! {0}", ship);
                NPCShipBehavior_Sector_Idle.AssignTo(ship);
            }
        }

        public override void DoTurn(NPCShip ship) {
            TryFollow(ship);
        }

    }
}
