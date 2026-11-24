using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class NPCShipBehavior_Sector_AvoidHostileShips : NPCShipBehavior {
        public const string BehaviorId = "Sector_AvoidHostileShips";
        public static int BehaviorIdH = Hashes.S(BehaviorId);

        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Sector_AvoidHostileShips());
        }

        public override string Id => BehaviorId;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Sector;
        private readonly List<Vector2> positions = new();
        public override bool CanHyperjump => true;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override void OnEnd(NPCShip ship) {
            ship.StopMoving(false);
        }

        public override void DoTurn(NPCShip ship) {
            if (ship.SO.Parent == null) { return; }

            var sector = ship.Sector;
            var shipsInSector = ship.SO.S.Query.NPCShipsAt.Ask(sector);

            if (TryFarthestPositionFromHostileShips(ship,
                    shipsInSector, out var position)) {
                // D.Warn("Avoiding hostile ships: {0} (hostiles: {1})",
                //     position, D.DumpCollection(positions));
                ship.FlyInLocalSpaceTo(position);
                if (!ship.WantsHyperspace && ship.NumHyperdrivesActual > 0) {
                    ship.SetWantsHyperspace(true);
                }
            }
        }

        private bool TryFarthestPositionFromHostileShips(NPCShip ship,
                List<NPCShip> others, out Vector2 position) {
            if (others == null) {
                position = default;
                return false;
            }
            positions.Clear();
            foreach (var other in others) {
                if (other.CanBeHostileTowards(ship)) {
                    var pos = other.SO.Location;
                    positions.Add(pos);
                }
            }

            var radius = ship.SO.Parent.ExitDist;

            Vector2 midPoint = Vectors.MidPoint(positions);

            Vector2 closestEnemy = Consts.V2Zero;
            float closestEnemyDistance = float.MaxValue;
            for (int i = positions.Count - 1; i >= 0; i--) {
                Vector2 point = positions[i];
                var dist = Vector2.SqrMagnitude(point - ship.SO.Location);
                if (dist < closestEnemyDistance) {
                    closestEnemyDistance = dist;
                    closestEnemy = point;
                }
            }

            Vector2 escapeAnchor;
            var distToClosestEnemy = Vector2.SqrMagnitude(closestEnemy - ship.SO.Location);
            var distToMidPoint = Vector2.SqrMagnitude(midPoint - ship.SO.Location);
            if (distToClosestEnemy < distToMidPoint) {
                escapeAnchor = closestEnemy;
            } else {
                escapeAnchor = midPoint;
            }

            var escapeDir = (ship.SO.Location - escapeAnchor).normalized;
            var escapePoint = ship.SO.Location;
            var maxSqrDistance = (radius - 5f) * (radius - 5f) ;
            var step = 5f;
            int attempts = 100;
            while (escapePoint.sqrMagnitude < maxSqrDistance && attempts-- > 0) {
                escapePoint += escapeDir * step;
            }
            // D.Warn("MidPoint: {0}, Esc Dir: {1}. ShipPos: {2}. Esc Pos: {3}. ClosestE: {4}, Positions: {5}",
            //     midPoint, escapeDir, ship.SO.Location, closestEnemy, escapePoint,
            //     D.DumpCollection(positions));
            position = escapePoint;
            return true;
        }

        public override bool IsCompatibleWith(NPCShip ship, SpaceSector sector) {
            return ship.NumThrustersActual > 0;
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
