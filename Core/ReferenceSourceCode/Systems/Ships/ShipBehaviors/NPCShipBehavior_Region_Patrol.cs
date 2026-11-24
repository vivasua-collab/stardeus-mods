using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using KL.Objects;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipBehavior_Region_Patrol : NPCShipBehavior {
        public const string BehaviorId = "Region_Patrol";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Region_Patrol());
        }
        public override string Id => BehaviorId;
        public override bool CanHyperjump => true;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Region;

        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override void DoTurn(NPCShip ship) {
            // 1. check if ship.memory.sectorpath is null, if so, create a random route
            if (Arrays.IsEmpty(ship.Memory.SectorPath)) {
                CreatePatrolRouteFor(ship);
                if (ship.Memory.SectorPath.Count == 0) {
                    #if UNITY_EDITOR
                    // D.Warn("Ship has no route after CreatePatrolRoute: {0}", ship);
                    #endif
                    ship.ChangeBehavior(NPCShipBehaviorScope.Region, false);
                }

                // D.Warn("Created route for ship: {0}: {1}: {2}", ship.Sector.Region,
                //     D.DumpCollection(ship.Memory.SectorPath), ship);
            }

            if (!ship.IsInHyperspace) {
                if (ship.WantsHyperspace) {
                    // done investigating the current sector
                    ship.EnterHyperspace();
                } else if (RollWantsHyperspace(ship,
                        Tunables.NPCShipBehavior_Region_Patrol_WantHyperspaceChance)) {
                    ship.SetWantsHyperspace(true);
                }
                return;
            }

            // 2. follow the route and jump around until the route is traveled
            if (ship.Memory.SectorPath.TryDequeue(out var nextJumpTo)) {
                ship.HyperjumpTo(nextJumpTo);
            }

            if (RollExitHyperspace(ship,
                    Tunables.NPCShipBehavior_Region_Patrol_ExitHyperspaceChance)) {
                ship.SetWantsHyperspace(false);
                ship.ExitHyperspace();
            }
        }

        public override bool IsCompatibleWith(NPCShip ship, SpaceSector sector) {
            if (A.S.Query.IsActiveQuestProvider.Ask(ship)) {
                // gounded!
                return false;
            }
            return true;
        }
    }
}
