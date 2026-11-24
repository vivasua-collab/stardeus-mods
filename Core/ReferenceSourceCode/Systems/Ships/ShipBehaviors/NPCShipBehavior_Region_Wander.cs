// #define NO_REST
using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
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
    public sealed class NPCShipBehavior_Region_Wander : NPCShipBehavior {
        public const string BehaviorId = "Region_Wander";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipBehavior_Region_Wander());
        }
        public override string Id => BehaviorId;
        public override bool CanHyperjump => true;
        public override NPCShipBehaviorScope Scope => NPCShipBehaviorScope.Region;
        public override bool CanInterruptForNotice(NPCShip observer, NPCShip target) {
            return true;
        }

        public override void OnStart(NPCShip ship) {
            // D.Warn("Starting region wanderer behavior for ship: {0} -> {1}",
            //     ship.Sector.Region, ship);
        }

        public override bool IsCompatibleWith(NPCShip ship, SpaceSector sector) {
            if (ship.NumLongRangeDrives > 0) { return true; }
            var hasRelay = SOUtils.HasHyperjumpRelay(sector.Region);
            // D.Warn("Region has relay: {0}, {1}", hasRelay, sector.Region);
            if (!hasRelay) {
                return false;
            }
            if (A.S.Query.IsActiveQuestProvider.Ask(ship)) {
                // gounded!
                return false;
            }

            return true;
        }

        public override void DoTurn(NPCShip ship) {

            // If ship is at the origin, find a target in a distant region
            // and travel there.
            // once target is reached, return to the origin
            // repeat

            if (Arrays.IsEmpty(ship.Memory.RegionPath)) {
                if (ship.Sector.Region == ship.Memory.OriginSector.Region) {
                    // find destination
                    // crawl from current region
                    // until we reach a location that either is too far,
                    // or restricted by difficulty
                    if (!CreateRegionRouteFor(ship, ship.Sector)) {
                        ship.ChangeBehavior(Scope, false);
                        return;
                    }
                } else {
                    // return
                    if (!CreateRegionRouteFor(ship, ship.Sector, ship.Memory.OriginSector)) {
                        ship.ChangeBehavior(Scope, false);
                        return;
                    }
                }
            }

            if (!ship.IsInHyperspace) {
                if (ship.WantsHyperspace) {
                    // done investigating the current sector
                    ship.EnterHyperspace();
                } else if (RollWantsHyperspace(ship,
                        Tunables.NPCShipBehavior_Region_Wander_WantHyperspaceChance)) {
                    ship.SetWantsHyperspace(true);
                }
                #if NO_REST
                ship.WantsHyperspace = true;
                #endif
            }

            if (ship.IsInHyperspace && ship.Sector.IsHyperjumpRelay) {
                DoNextRegionJump(ship);
                return;
            }

            // we are in hyperspace. We wan to get to a relay
            var path = ship.Memory.SectorPath ??= new();
            if (Arrays.IsEmpty(path)) {
                if (ship.Sector.IsHyperjumpRelay) {
                    // we are at the relay, let's pathfind the next
                    if (ship.Memory.RegionPath.TryPeek(out var reg)) {
                        var start = Rng.Unseeded.From(reg.Sectors);
                        if (TryFindPathToHyperjumpRelay(ship, start)) {
                            // we now have the path within the next region
                        }
                    }
                } else if (!TryFindPathToHyperjumpRelay(ship, ship.Sector)) {
                    // D.Warn("Failed to find a path to HJ relay from: {0} | R: {1}",
                    //     ship.Sector, ship.Sector.Region);
                    if (ship.IsInHyperspace) {
                        DoNextRegionJump(ship);
                    }
                    return;
                }
            }

            if (ship.IsInHyperspace) {
                // follow the path
                if (path.TryDequeue(out var sector)) {
                    ship.HyperjumpTo(sector);
                }

                if (RollExitHyperspace(ship,
                        Tunables.NPCShipBehavior_Region_Wander_ExitHyperspaceChance)) {
                    ship.SetWantsHyperspace(false);
                    ship.ExitHyperspace();
                }
                #if NO_REST
                if (ship.IsInHyperspace) {
                    ship.WantsHyperspace = true;
                    ship.ExitHyperspace();
                }
                #endif
            }

            return;
        }

        private void DoNextRegionJump(NPCShip ship) {
            if (ship.Memory.RegionPath.TryDequeue(out var nextRegion)) {
                SpaceSector sector = null;
                if (!Arrays.IsEmpty(ship.Memory.SectorPath)) {
                    if (ship.Memory.SectorPath.TryPeek(out var next)) {
                        if (next.Region != ship.Sector.Region) {
                            sector = next;
                        }
                    }
                }
                sector ??= nextRegion.InboundSector;
                // D.Warn("Doing a region jump for ship: {0}->{1} | {2}",
                //     ship.Sector.Region, nextRegion, ship);
                ship.HyperjumpTo(sector);
                ship.Memory.SectorPath?.Clear();
            }
        }

        private bool TryFindPathToHyperjumpRelay(NPCShip ship, SpaceSector sector) {
            SpaceSector target = null;
            var path = ship.Memory.SectorPath ??= new();
            path.Clear();
            foreach (var sec in sector.Region.Sectors) {
                if (sec.IsHyperjumpRelay) {
                    target = sec;
                    break;
                }
            }
            if (target == null) {
                return false;
            }
            return SOUtils.FindPath(sector, target, path);
        }

        private static bool ShouldSkipRegion(NPCShip ship, float minDifficulty,
                bool hasLongRangeDrive, SpaceRegion n) {
            bool shouldSkip = false;
            if (n.Biome.Difficulty < minDifficulty) {
                shouldSkip = true;
            }
            if (!hasLongRangeDrive) {
                // make sure that there is a hyperjump relay
                bool hasRelay = SOUtils.HasHyperjumpRelay(n);
                if (!hasRelay) {
                    // D.Warn("Skipping region without relay: {0} for ship without long range drives: {1}", n, ship);
                    shouldSkip = true;
                }
            }

            return shouldSkip;
        }


        private bool CreateRegionRouteFor(NPCShip ship, SpaceSector from) {
            var current = from.Region;
            var start = current;
            visited.Clear();
            visited.Add(start);
            // a ship from region with 80% difficulty will be able to go to
            // regions of no less than 60% difficulty
            float minDifficulty = ship.Memory.OriginSector.Region.Biome.Difficulty
                - Tunables.NPCShipBehavior_Region_Wander_MinDifficultyDiff;
            var hasLongRangeDrive = ship.NumLongRangeDrives > 0;
            var maxDepth = 20;
            var path = ship.Memory.RegionPath ??= new();
            path.Clear();
            while (current != null && maxDepth-- > 0) {
                links.Clear();
                links.AddRange(current.Links);
                Arrays.Shuffle(Rng.Unseeded, links);
                SpaceRegion next = null;
                foreach (var link in links) {
                    SpaceRegion n = (SpaceRegion)link.OtherSideOf(current);
                    if (ShouldSkipRegion(ship, minDifficulty, hasLongRangeDrive, n)) {
                        continue;
                    }
                    if (visited.Add(n)) {
                        next = n;
                        break;
                    }
                }
                if (next != null) {
                    path.Enqueue(next);
                }
                current = next;
            }
            if (ship.Memory.RegionPath.Count == 0) {
                #if UNITY_EDITOR
                // D.Warn("Failed creating region route for wanderer: {0}", ship);
                #endif
                ship.ChangeBehavior(NPCShipBehaviorScope.Region, false);
                if (!hasLongRangeDrive) {
                    ConsiderAddingLongRangeDrive(ship);
                } else {
                    #if UNITY_EDITOR
                    // D.ErrSoft("Failed creating region route for wanderer with long range drives: {0} | {1} ({2})",
                    //     ship, from, from.Region);
                    #endif
                }
                return false;
            }
            return true;
        }

        private void ConsiderAddingLongRangeDrive(NPCShip ship) {
            if (ship.IsDerelict || ship.IsInCombat ||
                    ship.IsPlayer || ship.NumLongRangeDrives > 0 ||
                    ship.Class.NumLongRangeDrivesCurve.Evaluate(1.0f) < 1f) {
                return;
            }
            if (!Rng.UChance(Tunables.NPCShipBehavior_Region_Wander_AddLongRangeDriveChance)) {
                return;
            }
            // replace fusion drive with long range drive
            IShipDevice drive = null;
            foreach (var dev in ship.CombatState.Devices) {
                if (dev.Device.TargetType.IdH == ShipWeaponTargetTypeIdH.ShipEngine_Hyperdrive) {
                    drive = dev;
                    break;
                }
            }
            bool replaced = false;
            if (drive != null) {
                ShipEngineDef engineToAdd = null;
                foreach (var engine in ShipEngineDef.All.Values.OrderBy(v => Rng.UValue)) {
                    if (engine.EngineType == ShipEngineType.HyperdriveLong &&
                            engine.CanAddFor(ship, out float w)) {
                        engineToAdd = engine;
                        break;
                    }
                }
                if (engineToAdd != null) {
                    ship.CombatState.RemoveDevice(drive);
                    ship.CombatState.AddEngine(engineToAdd);
                    replaced = true;
                }
            }

            if (!replaced) {
                D.Err("Failed replacing hyperdrive with long range drive for ship: {0} | {1}",
                    ship, D.DumpCollection(ship.CombatState.Devices));
            } else {
                D.Warn("Replaced hyperdrive with long range drive for ship: {0} | {1}",
                    ship, D.DumpCollection(ship.CombatState.Devices));
            }
            NPCShipGen.ValidateShipEngineCounts(ship, false);
        }

        private bool CreateRegionRouteFor(NPCShip ship, SpaceSector from, SpaceSector to) {
            var start = from.Region;
            var target = to.Region;
            // a ship from region with 80% difficulty will be able to go to
            // regions of no less than 60% difficulty
            float minDifficulty = ship.Memory.OriginSector.Region.Biome.Difficulty - 0.2f;
            var hasLongRangeDrive = ship.NumLongRangeDrives > 0;
            var path = ship.Memory.RegionPath ??= new();
            path.Clear();

            var foundPath = SOUtils.FindPath(start, target, path, (reg) => {
                if (reg.Biome.Difficulty < minDifficulty) {
                    return false;
                }
                if (!hasLongRangeDrive && !SOUtils.HasHyperjumpRelay(reg)) {
                    return false;
                }
                return true;
            });

            if (!foundPath) {
                // D.Warn("Failed creating region route from {0} to {1}", start, target);
                return false;
            }
            return true;
        }
    }
}
