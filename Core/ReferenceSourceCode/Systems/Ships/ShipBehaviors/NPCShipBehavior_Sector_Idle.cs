using System;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipBehavior_Sector_Idle : NPCShipBehavior {
        public const string BehaviorId = "Sector_Idle";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_Idle());
        }
        public override string Id => BehaviorId;
        public override bool CanHyperjump => true;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override void DoTurn(NPCShip ship) {
        }

        public static void AssignTo(NPCShip ship) {
            ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                NPCShipBehaviorScope.Sector);
        }
    }
}
