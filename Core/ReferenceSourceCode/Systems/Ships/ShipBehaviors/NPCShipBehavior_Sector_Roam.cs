using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipBehavior_Sector_Roam : NPCShipBehavior {
        public const string BehaviorId = "Sector_Roam";
        public const string Key_Long_RoamAvailableAfter = "roam.available.after";
        public static int BehaviorIdH = Hashes.S(BehaviorId);
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_Roam());
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
            if (ship.SO.Parent == null) { return; }
            if (ship.HasLocalTarget
                    && Vectors.SqrDistance(ship.LocalTarget, ship.SO.Location) >= 0.01f) {
                return;
            }

            if (ship.Memory.TryCustom(Key_Long_RoamAvailableAfter, out long ticks)) {
                if (G.Ticks > ticks) { return; }
                ship.Memory.Custom.RemoveLong(Key_Long_RoamAvailableAfter);
            } else {
                ship.Memory.Custom.SetLong(Key_Long_RoamAvailableAfter,
                    G.Ticks + (long) Rng.URange(
                        Consts.TicksPer30Minutes, Consts.TicksPer90Minutes));
            }
            // set random target
            var target = SOUtils.RandomLocationNearChild(
                ship.Sector.SO, Rng.Unseeded, 1f, 5f, 0.25f);
            ship.FlyInLocalSpaceTo(target);
        }

        public override bool IsCompatibleWith(NPCShip ship, SpaceSector sector) {
            if (ship.NumThrustersActual < 1) {
                return false;
            }
            return true;
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
