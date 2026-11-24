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
    /// <summary>
    /// Almost identical to Region_Patrol, but if a hyperjump relay is found,
    /// the ship will jump to a random adjacent region
    /// </summary>
    public sealed class NPCShipBehavior_Region_RandomWalk : NPCShipBehavior {
        public const string BehaviorId = "Region_RandomWalk";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Region_RandomWalk());
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
                // D.Warn("Created route for ship: {0}: {1}: {2}", ship.Sector.Region,
                //     D.DumpCollection(ship.Memory.SectorPath), ship);
            }

            if (!ship.IsInHyperspace) {
                if (ship.WantsHyperspace) {
                    // done investigating the current sector
                    ship.EnterHyperspace();
                } else if (RollWantsHyperspace(ship,
                        Tunables.NPCShipBehavior_Region_RandomWalk_WantHyperspaceChance)) {
                    ship.SetWantsHyperspace(true);
                }
                return;
            }

            if (ship.Sector.IsHyperjumpRelay) {
                var minDifficulty = ship.Memory.OriginSector.Biome.Difficulty
                    - Tunables.NPCShipBehavior_Region_Wander_MinDifficultyDiff;
                links.Clear();
                foreach (var link in ship.Sector.Region.Links) {
                    var other = (SpaceRegion) link.OtherSideOf(ship.Sector.Region);
                    if (other.Biome.Difficulty < minDifficulty) { continue; }
                    if (ship.NumLongRangeDrives == 0) {
                        if (!SOUtils.HasHyperjumpRelay(other)) {
                            continue;
                        }
                    }
                    links.Add(link);
                }
                if (links.Count > 0) {
                    // links.AddRange(ship.Sector.Region.Links);
                    Arrays.Shuffle(Rng.Unseeded, links);
                    SpaceRegion target = null;
                    foreach (var link in links) {
                        var reg = (SpaceRegion) link.OtherSideOf(ship.Sector.Region);
                        if (reg != ship.Memory.PreviousRegion) {
                            target = reg;
                            break;
                        }
                    }
                    target ??= ship.Memory.PreviousRegion;
                    ship.Memory.SectorPath.Clear();
                    ship.HyperjumpTo(target.InboundSector);
                    return;
                }
            }

            // 2. follow the route and jump around until the route is traveled
            if (ship.Memory.SectorPath.TryDequeue(out var nextJumpTo)) {
                ship.HyperjumpTo(nextJumpTo);
            }

            if (RollExitHyperspace(ship,
                    Tunables.NPCShipBehavior_Region_RandomWalk_ExitHyperspaceChance)) {
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
