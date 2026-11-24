using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipBehavior_Sector_Orbit : NPCShipBehavior {
        public const string BehaviorId = "Sector_Orbit";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_Orbit());
        }
        public override string Id => BehaviorId;
        public override bool CanHyperjump => true;

        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(false);
        }

        public override void DoTurn(NPCShip ship) {
            if (ship.SO.Parent != null && (!ship.HasLocalTarget
                    || Vectors.SqrDistance(ship.LocalTarget, ship.SO.Location) < 0.01f)) {
                // set random target
                var loc = ship.SO.Location;
                if (loc.sqrMagnitude <= 1f) {
                    loc = SOUtils.RandomLocationInside(ship.Sector.SO, Rng.Unseeded);
                }
                ship.FlyInLocalSpaceTo(
                    Maths.NextPointOnCircle(loc, 2f * ((ship.Id % 2 == 1) ? -1 : 1)));
            }
        }

        public override bool IsCompatibleWith(NPCShip ship, SpaceSector sector) {
            if (ship.NumThrusters < 1) {
                return false;
            }
            if (sector.Biome.Template is SectorTemplate tpl) {
                if (tpl.HasOrbit) {
                    return true;
                }
            }
            return false;
        }
        public static void AssignTo(NPCShip ship) {
            if (ship.NumThrustersActual > 0) {
                ship.SetBehavior(Registries.NPCShipBehaviors.FastGet(BehaviorIdH),
                    NPCShipBehaviorScope.Sector);
            } else {
                NPCShipBehavior_Sector_Idle.AssignTo(ship);
            }
        }
    }
}
