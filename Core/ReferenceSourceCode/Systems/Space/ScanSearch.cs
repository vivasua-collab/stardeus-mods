#if !PRODUCTION_BUILD
// #define SCAN_DEBUG
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    /// <summary>
    /// Outward ring search used by radar scanning (<see cref="ScanSys"/>). Starting from the
    /// player's region, evaluates every region at the current hop distance before expanding to
    /// the next ring, so results creep outward one region at a time instead of jumping across
    /// the map. See Docs/Architecture/Systems/ScannerSystem.md.
    /// </summary>
    public sealed class ScanSearch {
        private readonly GameState S;

        private readonly Queue<SpaceSector> sectorQueue = new();
        private readonly List<SpaceRegion> ring = new();
        private readonly List<SpaceRegion> nextRing = new();
        private readonly HashSet<int> visitedSectors = new();
        private readonly HashSet<int> visitedRegions = new();
        private readonly List<SpaceSector> injectCandidates = new();
        private readonly List<float> injectChances = new();
        private Curve hopChanceCurve;

        public ScanSearch(GameState s) {
            S = s;
        }

        private static SpaceSector EntrySectorFor(SpaceRegion region) {
            foreach (var sec in region.Sectors) {
                if (sec.IsHyperjumpRelay) {
                    return sec;
                }
            }
            return region.InboundSector;
        }

        private SpaceSector WalkRegions(Func<SpaceSector, HashSet<int>, bool, SpaceSector> search,
                bool fallback, Func<SpaceRegion, int, SpaceSector> tryInject = null) {
            var playerSector = S.Universe.Player.Parent.Sector;
            visitedSectors.Clear();
            visitedRegions.Clear();

            var currentRing = ring;
            var next = nextRing;
            currentRing.Clear();
            next.Clear();

            var homeRegion = playerSector.Region;
            visitedRegions.Add(homeRegion.Id);
            currentRing.Add(homeRegion);

            int hop = 0;
            while (currentRing.Count > 0) {
                #if SCAN_DEBUG
                D.Warn("[ScanSearch] Ring {0} ({1} regions): {2}", hop, currentRing.Count,
                    string.Join(", ", currentRing));
                #endif
                foreach (var region in currentRing) {
                    var start = hop == 0 ? playerSector : EntrySectorFor(region);
                    sectorQueue.Clear();
                    var found = search(start, visitedSectors, fallback);
                    if (found != null) {
                        #if SCAN_DEBUG
                        D.Warn("[ScanSearch] Found existing at ring {0}: {1}", hop, found);
                        #endif
                        return found;
                    }
                }
                if (tryInject != null) {
                    foreach (var region in currentRing) {
                        var found = tryInject(region, hop);
                        if (found != null) {
                            #if SCAN_DEBUG
                            D.Warn("[ScanSearch] Injected at ring {0}: {1}", hop, found);
                            #endif
                            return found;
                        }
                    }
                }
                next.Clear();
                foreach (var region in currentRing) {
                    foreach (var link in region.Links) {
                        var n = (SpaceRegion) link.OtherSideOf(region);
                        if (visitedRegions.Add(n.Id)) {
                            next.Add(n);
                        }
                    }
                }
                (currentRing, next) = (next, currentRing);
                hop++;
            }
            return null;
        }

        public SpaceSector FindMat(MatType mat) {
            return WalkRegions(
                (start, visited, fallback) => CrawlSectorForTargetMat(start, visited, mat, fallback),
                false,
                (region, hop) => TryInjectDeposit(region, mat, hop));
        }

        public SpaceSector FindAnomaly() {
            var found = WalkRegions(CrawlSectorForAnomaly, false);
            return found ?? WalkRegions(CrawlSectorForAnomaly, true);
        }

        public SpaceSector FindDerelictShip() {
            var found = WalkRegions(CrawlSectorForDerelictShip, false);
            return found ?? WalkRegions(CrawlSectorForDerelictShip, true);
        }

        public SpaceSector FindSpaceStation() {
            var found = WalkRegions(CrawlSectorForSpaceStation, false);
            return found ?? WalkRegions(CrawlSectorForSpaceStation, true);
        }

        public SpaceSector FindRelay() {
            var found = WalkRegions(CrawlSectorForRelay, false);
            return found ?? WalkRegions(CrawlSectorForRelay, true);
        }

        // Injects the searched deposit into an unexplored planet/asteroid sector of the given
        // region, but only when that region has no deposits of the material at all - discovered
        // or undiscovered. The chance grows with hop distance from the player, so the further
        // the ring walk has to travel, the more likely it is to spawn the material nearby
        // instead of pointing at a distant real deposit.
        // See Docs/Architecture/Systems/ScannerSystem.md.
        private SpaceSector TryInjectDeposit(SpaceRegion region, MatType mv, int hop) {
            var dep = mv.Deposit;
            if (dep == null || dep.NearbySpawnChance <= 0f) {
                return null;
            }
            foreach (var sec in region.Sectors) {
                if (sec.Biome.HasDepositsFor(mv)) {
                    return null;
                }
            }
            injectCandidates.Clear();
            injectChances.Clear();

            // Slightly increase chances on lower difficulty, don't reduce on higher
            var chanceMul = 1.5f - S.Difficulty.Reduction * 0.5f;
            chanceMul = Mathf.Clamp(chanceMul, 1f, 1.5f);

            foreach (var sec in region.Sectors) {
                var so = sec.SO;
                if (so.IsVisited) { continue; }
                // not generated yet
                if (so.Type.IdH != SpaceObjectTypeIdH.Sector) { continue; }
                if (sec.Biome.Template is not SectorTemplate tpl) { continue; }
                if (tpl.Type != SpaceObjectTypeId.Planet
                        && tpl.Type != SpaceObjectTypeId.AsteroidZone) { continue; }
                if (!dep.Matches(sec, out var secChance)) { continue; }
                secChance *= chanceMul;
                injectCandidates.Add(sec);
                injectChances.Add(secChance);
            }
            if (injectCandidates.Count == 0) {
                return null;
            }
            hopChanceCurve ??= CurveDef.FastGet(
                CurveIdH.Scanning_RegionHops_NearbyDepositChanceMul);
            var rng = new Rng(Hashes.Quick(region.Seed, mv.IdH));
            var idx = rng.Range(0, injectCandidates.Count);
            var chance = dep.NearbySpawnChance * injectChances[idx]
                * hopChanceCurve.Evaluate(hop);
            #if SCAN_DEBUG
            D.Warn("[ScanSearch] Inject roll for {0} in {1} at hop {2}: {3} candidates, p={4}",
                mv.Id, region.Name, hop, injectCandidates.Count, chance);
            #endif
            if (!rng.Chance(chance)) {
                return null;
            }
            var target = injectCandidates[idx];
            target.Biome.AddAssignable(dep, checkDifficulty: false);
            return target;
        }

        private SpaceSector CrawlSectorForTargetMat(SpaceSector start, HashSet<int> visited,
                MatType mat, bool fallback) {
            visited.Add(start.Id);
            sectorQueue.Enqueue(start);
            // Breadth First Search
            SpaceSector backup = null;
            while (sectorQueue.TryDequeue(out var sector)) {
                if (sector.Biome.HasDepositsFor(mat)) {
                    if (sector.SO.HasKnowledgeOf(mat)) {
                        if (fallback) {
                            backup ??= sector;
                        }
                    } else {
                        // Found!
                        return sector;
                    }
                }
                // scan all neighbors
                foreach (var link in sector.Links) {
                    var n = (SpaceSector) link.OtherSideOf(sector);
                    if (visited.Add(n.Id)) {
                        sectorQueue.Enqueue(n);
                    }
                }
            }
            return backup;
        }

        private SpaceSector CrawlSectorForAnomaly(SpaceSector start, HashSet<int> visited, bool fallback) {
            visited.Add(start.Id);
            sectorQueue.Enqueue(start);

            // Breadth First Search
            SpaceSector backup = null;
            while (sectorQueue.TryDequeue(out var sector)) {
                var anomalyPOI = POIConfig.Get(SpaceObjectTypeId.Anomaly);
                if (sector.Biome.HasAssignable(anomalyPOI, false)) {
                    if (!sector.SO.IsVisited && !sector.SO.HasChildren) {
                        // Found!
                        return sector;
                    } else if (fallback && backup == null && HasAnomalyLeft(sector.SO)) {
                        backup = sector;
                    }
                }
                // scan all neighbors
                foreach (var link in sector.Links) {
                    var n = (SpaceSector) link.OtherSideOf(sector);
                    if (visited.Add(n.Id)) {
                        sectorQueue.Enqueue(n);
                    }
                }
            }
            return backup;
        }

        // Ship-content zones (derelict, trade) spawn their ships outside SO.Children until the
        // sector is visited, so a generated sector type is what marks them as already found
        private static bool IsUnrevealed(SpaceSector sector) {
            var so = sector.SO;
            if (so.IsVisited || so.HasChildren) { return false; }
            var type = so.Type.IdH;
            return type == SpaceObjectTypeIdH.Sector || type == SpaceObjectTypeIdH.SectorUnknown;
        }

        // A known result is only worth pointing at while it still holds what was scanned for.
        // A looted zone stays a DerelictZone template, and the walk starts at the player's own
        // sector, so the scan kept pointing at the empty zone the ship sat in (STA-1862).
        private bool HoldsApproachableDerelict(SpaceSector sector) {
            if (!S.Sys.NPCShip.SectorShips.TryGetValue(sector, out var ships)) {
                return false;
            }
            foreach (var ship in ships) {
                if (!ship.IsDerelict || ship.IsPlayer) { continue; }
                // a quest leads the player to its own derelict
                if (S.Query.IsActiveQuestProvider.Ask(ship)) { continue; }
                if (S.Query.GetQuestStepTargets.Ask(ship).Length > 0) { continue; }
                return true;
            }
            return false;
        }

        // Expeditions remove an explored anomaly from its sector
        private static bool HasAnomalyLeft(SpaceObject so) {
            if (!so.HasChildren) { return false; }
            foreach (var child in so.Children) {
                if (child.Type.IdH == SpaceObjectTypeIdH.Anomaly) {
                    return true;
                }
            }
            return false;
        }

        private SpaceSector CrawlSectorForDerelictShip(SpaceSector start, HashSet<int> visited, bool fallback) {
            visited.Add(start.Id);
            sectorQueue.Enqueue(start);

            // Breadth First Search
            SpaceSector backup = null;
            while (sectorQueue.TryDequeue(out var sector)) {
                if (IsUnrevealed(sector)) {
                    if (sector.Biome.Template.Id == SpaceObjectTypeId.DerelictZone) {
                        return sector;
                    }
                } else if (fallback && backup == null && HoldsApproachableDerelict(sector)) {
                    // any known sector, not only derelict zones: combat leaves derelicts too
                    backup = sector;
                }
                // scan all neighbors
                foreach (var link in sector.Links) {
                    var n = (SpaceSector) link.OtherSideOf(sector);
                    if (visited.Add(n.Id)) {
                        sectorQueue.Enqueue(n);
                    }
                }
            }
            return backup;
        }

        private SpaceSector CrawlSectorForRelay(SpaceSector start, HashSet<int> visited, bool fallback) {
            visited.Add(start.Id);
            sectorQueue.Enqueue(start);

            // Breadth First Search
            SpaceSector backup = null;
            while (sectorQueue.TryDequeue(out var sector)) {
                if (sector.IsHyperjumpRelay) {
                    if (!sector.SO.IsVisited && !sector.SO.HasChildren) {
                        return sector;
                    } else if (fallback) {
                        backup = sector;
                    }
                }
                // scan all neighbors
                foreach (var link in sector.Links) {
                    var n = (SpaceSector) link.OtherSideOf(sector);
                    if (visited.Add(n.Id)) {
                        sectorQueue.Enqueue(n);
                    }
                }
            }
            return backup;
        }

        private SpaceSector CrawlSectorForSpaceStation(SpaceSector start, HashSet<int> visited, bool fallback) {
            visited.Add(start.Id);
            sectorQueue.Enqueue(start);

            var spaceStationPOI = POIConfig.Get(SpaceObjectTypeId.SpaceStation);

            // Breadth First Search
            SpaceSector backup = null;
            while (sectorQueue.TryDequeue(out var sector)) {
                if (IsUnrevealed(sector)) {
                    if (sector.Biome.Template.Id == SpaceObjectTypeId.TradeZone) {
                        return sector;
                    }
                    if (sector.Biome.HasAssignable(spaceStationPOI, false)) {
                        return sector;
                    }
                } else if (fallback && backup == null) {
                    if (sector.Biome.Template.Id == SpaceObjectTypeId.TradeZone) {
                        backup = sector;
                    }
                    if (sector.Biome.HasAssignable(spaceStationPOI, false)) {
                        backup = sector;
                    }
                }
                // scan all neighbors
                foreach (var link in sector.Links) {
                    var n = (SpaceSector) link.OtherSideOf(sector);
                    if (visited.Add(n.Id)) {
                        sectorQueue.Enqueue(n);
                    }
                }
            }
            return backup;
        }
    }
}
