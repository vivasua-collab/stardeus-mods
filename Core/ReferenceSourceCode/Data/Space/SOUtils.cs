using System;
using System.Collections.Generic;
using System.Linq;
using Game.Achievements;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Factions;
using Game.Input;
using Game.Procgen.Space;
using Game.Systems;
using Game.Systems.Areas;
using Game.Systems.Quests;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using UnityEngine;

namespace Game.Data.Space {
    public static class SOUtils {
        [SelfInit]
        public static void Cleanup() {
            visited.Clear();
            cameFrom.Clear();
            res.Clear();
            priorityQueue.Clear();
            distances.Clear();
            looksByTypeCache.Clear();
        }
        private static readonly HashSet<ISpaceEntity> visited = new();
        private static readonly Dictionary<ISpaceEntity, ISpaceEntity> cameFrom = new();
        private static readonly List<ISpaceEntity> res = new();
        private static readonly Dictionary<ISpaceEntity, float> distances = new();
        private static readonly SimplePriorityQueue<ISpaceEntity> priorityQueue = new(256);
        private static Dictionary<string, List<SOLooks>> looksByTypeCache = new();
        // Set when a sector nuke goes off; the sector stays barren for good (STA-1683)
        public const string NukedKey = "Nuked";
        public static bool IsNuked(SpaceObject so) {
            return so.HasData && so.Data.GetBool(NukedKey, false);
        }

        public static bool CanShuttleReachInSpace(SpaceObject shuttle,
                SpaceObject target,
                out bool hasReached, float reachDist) {
            var from = shuttle.Location;
            var to = target.Location;
            var dist = Vectors.Distance(from, to);
            var spread = Tunables.StarmapSpaceObjectSpread;
            var scaledDist = dist * spread;
            hasReached = scaledDist < 5;
            //D.Warn("Dist: {0} / Scaled: {1} / Reach: {2} * {3}",
                //dist, scaledDist, shuttleReachDist, reachDistMul);
            // return scaledDist < reachDist;
            return true;
        }
        public static bool CanShuttleReachInSpace(Vector2 from, Vector2 target,
                out bool hasReached, float reachDist) {
            var dist = Vectors.Distance(from, target);
            var spread = Tunables.StarmapSpaceObjectSpread;
            var scaledDist = dist * spread;
            hasReached = scaledDist < 5;
            //D.Warn("Dist: {0} / Scaled: {1} / Reach: {2} * {3}",
                //dist, scaledDist, shuttleReachDist, reachDistMul);
            // return scaledDist < reachDist;
            return true;
        }

        public static bool HasNonPlayerShips(List<NPCShip> ships) {
            var cnt = ships?.Count ?? 0;
            if (cnt == 1 && ships.Contains(A.Player.Ship)) {
                return false;
            }
            return cnt > 0;
        }

        public static bool CanShuttleReachInSpace(Expedition exp, Vector3 target,
                out bool hasReached, float reachDistMul = 1f) {
            if (exp.Shuttle?.SpaceObject == null) {
                D.Err("No shuttle or no space object in reach check! {0}/{1}",
                    exp.Shuttle, exp.Shuttle?.SpaceObject);
                hasReached = false;
                return false;
            }
            var shuttleReachDist = exp.Shuttle.SpaceObject.Parent?
                .Type.ShuttleReachDist ?? 300;
            return CanShuttleReachInSpace(exp.Shuttle.SpaceObject.Location,
                target, out hasReached, shuttleReachDist * reachDistMul);
        }

        /// <returns>true if reached</returns>
        public static bool MoveTowards(SpaceObject so, SpaceObject target,
                Vector2 offset, float speed) {
            if (so == null) {
                D.Err("Trying to move null space object towards! {0}", target);
                return true;
            }

            var scale = Tunables.StarmapSpaceObjectSpread;
            var dist = Vectors.Distance(so.Location, target.Location + offset) * scale;
            // This is pixels in starmap UI
            if (dist > 3) {
                var shutSpeed = Tunables.SpaceTravelFlightSpeed * speed;
                so.Location = Vector2.MoveTowards(
                    so.Location, target.Location + offset,
                        shutSpeed / so.S.Clock.TickDuration);
                return false;
            }
            return true;
        }

        private static Curve factionRelationshipRelayTaxCurve;
        private static Curve sectorDiffRelayDiscountCurve;

        public static float HyperRelayToll(GameState s, SpaceObject relay,
                out float baseCost,
                out float taxPercent,
                out float discountPercent) {
            if (relay == null) {
                D.Err("Trying to get HyperRelayToll for null relay!");
                baseCost = 0f;
                taxPercent = 0f;
                discountPercent = 0;
                return 0f;
            }
            if (factionRelationshipRelayTaxCurve == null) {
                factionRelationshipRelayTaxCurve = CurveDef.FastGet(CurveIdH.Economy_FactionAttitude_RelayTax);
                sectorDiffRelayDiscountCurve = CurveDef.FastGet(CurveIdH.Economy_SectorDifficulty_RelayDiscount);
            }
            var sec = SectorFor(relay);
            var diff = sec.Region.Biome.Difficulty;
            discountPercent = sectorDiffRelayDiscountCurve.Evaluate(diff);
            var att = s.Query.FactionAttitude.Ask(relay.Faction, FactionDef.Player);
            taxPercent = factionRelationshipRelayTaxCurve.Evaluate(att);
            baseCost = relay.Faction.HyperjumpRelayBaseToll * s.Difficulty.SoftLevel;
            var finalCost = baseCost * (1f + taxPercent);
            finalCost = finalCost * (1f - discountPercent);
            return finalCost;
        }

        public static SpaceRegion RegionFor(SpaceObject so) {
            if (so == null) {
                D.Err("Null space object! Can't get a sector!");
                return null;
            }
            if (so.Region != null) {
                return so.Region;
            }
            if (so.Parent?.Region != null) {
                return so.Parent.Region;
            }
            if (so.Parent?.Sector != null) {
                return so.Parent.Sector.Region;
            }
            D.Err("Could not get a region from SO: {0}", so);
            return null;
        }
        public static SpaceSector SectorFor(SpaceObject so) {
            if (so == null) {
                D.Err("Null space object! Can't get a sector!");
                return null;
            }
            if (so.Sector != null) {
                return so.Sector;
            }
            if (so.Parent?.Sector != null) {
                return so.Parent.Sector;
            }
            if (so.Region != null) {
                return so.Region.InboundSector;
            }
            D.Err("Could not get a sector from SO: {0}", so);
            return null;
        }

        public static float SectorDifficultyOf(SpaceObject so, SpaceObject fallback) {
            if (so == null) {
                if (fallback != null) {
                    return SectorDifficultyOf(fallback, null);
                }
                D.Err("Getting Sector difficulty for null SO without fallback!");
                return 0.5f;
            }
            if (so.SpaceEntity == null) {
                return SectorDifficultyOf(so.Parent, fallback);
            }
            if (so.SpaceEntity.Biome == null) {
                D.Err("SpaceEntity has no biome! {0} | {1}", so, so.SpaceEntity);
                return 0.5f;
            }
            return so.SpaceEntity.Biome.Difficulty;
        }

        public static void RemoveSO(SpaceObject so) {
            // D.Warn("Removing space object: {0} (parent: {1})", so, so.Parent);
            if (so == null) { return; }
            if (so.Type.IdH == SpaceObjectTypeIdH.ShipDerelict && so.Parent != null) {
                so.Parent.ChangePOIs(so.Type.IdH, -1, so.Parent.HasKnowledge);
            }
            if (so.Type.IdH == SpaceObjectTypeIdH.Signal && so.Parent != null) {
                so.Parent.ChangePOIs(SpaceObjectTypeIdH.Signal, -1,
                    syncKnowledge: true);
            }
            if (so.Parent != null) {
                if (!so.Parent.RemoveChild(so)) {
                    D.Err("Failed removing space object from parent: {0} | {1}",
                        so, so.Parent);
                }
            }
            if (!so.S.Universe.ObjectsById.Remove(so.Id)) {
                D.ErrSoft("Failed removing space object from universe: {0}", so);
            }
            so.S.Sig.SpaceObjectRemoved.Send(so);
        }

        public static string FullName(SpaceObject so, bool link) {
            if (so == null) {
                D.Err("Trying to get full name from a null SpaceObject. Link? {0}", link);
                return null;
            }
            switch (so.Layer) {
                case UniverseLayer.Sector: {
                    return T.SectorName_V1.F(link ? so.StarmapLink : so.DisplayName);
                }
                case UniverseLayer.Region: {
                    return T.RegionName_V1.F(link ? so.StarmapLink : so.DisplayName);
                }
                case UniverseLayer.Universe: {
                    return T.SpaceObjectUniverse;
                }
                default: return link ? so.StarmapLink : so.DisplayName;
            }
        }

        public static SpaceSector GetPlayerSector(GameState s) {
            var parent = s.Universe.HyperspaceLocation ?? s.Universe.Player.Parent;
            if (parent == null) {
                D.Err("No parent for player in GetPlayerSector!");
                return null;
            }
            if (parent.Sector == null) {
                D.Err("No sector for player parent in GetPlayerSector! This can cause serious errors! Parent: {0}", parent);
            }
            return parent.Sector;
        }

        public static string RegionSectorName(SpaceSector sector) {
            return $"{Texts.LinkToStarmap(sector.Region.SO, T.RegionName_V1.F(sector.Region.SO.DisplayName))} {GlyphMap.ArrowRight} {Texts.LinkToStarmap(sector.SO, T.SectorName_V1.F(sector.SO.DisplayName))}";
        }
        public static bool HasHyperjumpRelay(SpaceObject to) {
            if (!to.HasChildren) { return false; }
            if (to.Region == null) { return false; }
            foreach (var child in to.Children) {
                if (child.Sector.IsHyperjumpRelay) {
                    return true;
                }
            }
            return false;
        }

        // The hyperspace layer a jump to this target needs, decided from where the ship is now.
        // Never use target.Parent.Layer: it is "Region" for every sector, including sectors in
        // other regions, which would let region-only drives reach them
        public static string JumpLayerFor(SpaceObject target) {
            if (target.Region != null) {
                return UniverseLayer.Universe;
            }
            return RegionFor(target) == RegionFor(A.PlayerSector)
                ? UniverseLayer.Region
                : UniverseLayer.Universe;
        }

        public static bool CanJumpTo(SpaceObject so, out bool isWarp) {
            isWarp = false;
            if (so == null) { return false; }
            if (so.Sector == null && so.Region == null) { return false; }
            var s = so.S;
            var loc = s.Universe.HyperspaceLocation ?? s.Universe.Player.Parent;
            var sector = loc?.Sector;
            if (sector == null) { return false; }
            if (so.Sector != null) {
                foreach (var link in sector.Links) {
                    if (so.Sector == link.OtherSideOf(loc.Sector)) {
                        // we can jump to this sector, it is adjacent
                        return true;
                    }
                }
            } else if (so.Region != null) {
                var reg = loc.Parent.Region;
                foreach (var link in reg.Links) {
                    if (so.Region == link.OtherSideOf(reg)) {
                        // adjacent region
                        return true;
                    }
                }
            }
            // check if we have a charged warp drive in current layer
            var state = s.Sys.ShipNav.HyperspaceStateFor(so.Parent);
            isWarp = true;
            return state.EnginesWarp > 0;
        }
        public static bool WarpToInstant(GameState s, SpaceObject from, SpaceObject to) {
            var queue = s.Query.GetHyperjumpQueue.Ask();
            queue.ExecuteMove(to);
            return true;
            // return s.Sys.Ship.TryEnterObject(to, from);
        }

        public static bool WarpTo(GameState s, SpaceObject from, SpaceObject to) {
            if (A.IsInHyperspace) {
                return false;
            }
            // A warp leaves the sector like a hyperjump does: derelicts are cut loose first,
            // and an Asimov law that forbids it stops the warp (STA-1891)
            if (!s.Sys.Areas.TryAbandonDerelicts(out _, leavingSector: true)) {
                return false;
            }
            bool warped;
            if (s.Sys.Ship.TryEnterObject(to, from)) {
                s.Sys.ShipNav.LeaveSectorBehind();
                UISounds.PlayHyperjump();
                s.UIQuery.RebuildStarmap.Ask(true, true);
                if (!UIShowing.Starmap) {
                    s.Sig.EnteredHyperspace.Send(false);
                }
                The.SysSig.SettingChanged.Send(Pref.GNebulaOn);
                warped = true;
            } else {
                D.Err("Failed entering black hole: {0} -> {1}", from, to);
                warped = false;
            }

            return warped;
        }

        public static Vector2 RandomLocationNearChild(SpaceObject obj, Rng rng,
                float distFrom, float distTo, float chanceEmptySpace) {
            var c = Caches.SpaceObjectList;
            c.Clear();
            if (Rng.UChance(chanceEmptySpace)) {
                return RandomLocationInside(obj, rng);
            }
            foreach (var child in obj.Children) {
                if (child == obj) { continue; }
                // don't fly to planet closeup
                // if (child.Type.StarmapDecoration) { continue; }
                if (child.Type.IdH == SpaceObjectTypeIdH.Player) {
                    continue;
                }
                c.Add(child);
            }
            if (c.Count == 0) {
                return RandomLocationInside(obj, rng);
            }
            var randomChild = rng.From(c);
            var loc = randomChild.Location
                + rng.InsideUnitCircle().normalized
                    * rng.Range(distFrom, distTo);
            return loc;
        }

        public static Vector2 RandomLocationInside(SpaceObject obj, Rng rng,
                float maxDistToEdgePct = 1f) {
            var rngInside = rng.InsideUnitCircle();
            // D.Warn("Rng Inside: {0}", rngInside);
            var exitDist = Math.Max(1f, obj.ExitDist) * maxDistToEdgePct;
            // D.Warn("Exit dist: {0}", exitDist);
            var res = rngInside * exitDist;
            // D.Warn("Final: {0}", res);
            return res;
        }
        public static Vector2 RandomLocationOnExitRing(SpaceObject obj, Rng rng) {
            var rngInside = rng.InsideUnitCircle().normalized;
            // D.Warn("Rng Inside XR: {0}", rngInside);
            var exitDist = Math.Max(1f, obj.ExitDist);
            // D.Warn("Exit dist XR: {0}", exitDist);
            var res = rngInside * exitDist;
            // D.Warn("Final XR: {0}", res);
            return res;
        }

        public static bool HasHyperjumpRelay(SpaceRegion region) {
            foreach (var sec in region.Sectors) {
                if (sec.IsHyperjumpRelay) {
                    return true;
                }
            }
            return false;
        }

        public static bool FindPath<T>(T from, T to,
                Queue<T> path, Func<T, bool> isPassable = null) where T : ISpaceEntity, new() {
            cameFrom.Clear();
            visited.Clear();
            res.Clear();
            path.Clear();
            priorityQueue.Clear();
            distances.Clear();
            visited.Add(from);
            distances[from] = 0;
            priorityQueue.Enqueue(from, 0);

            if (isPassable != null) {
                if (!isPassable(from) || !isPassable(to)) {
                    return false;
                }
            }

            while (priorityQueue.TryDequeue(out var current)) {
                if (current.Id == to.Id) {
                    // Reconstruct path
                    var step = current;
                    while (step != null) {
                        res.Add(step);
                        cameFrom.TryGetValue(step, out step);
                    }
                    ISpaceEntity prevNode = null;
                    for (int i = res.Count - 1; i >= 0; i--) {
                        T node = (T) res[i];
                        if (prevNode != null) {
                            if (!SpaceLink.ExistsFor(prevNode, node)) {
                                D.Err("Trying to reconstruct path with step without a link! {0}<->{1}",
                                    prevNode, node);
                            }
                        }
                        if (path.Count > 0 || node.Id != from.Id) {
                            path.Enqueue(node);
                        }
                        prevNode = node;
                    }
                    // D.Warn("Path from {0}->{1}: {2}", from, to, D.DumpCollection(path));
                    return true;
                }

                foreach (var link in current.Links) {
                    var neighbor = link.OtherSideOf(current);
                    if (visited.Add(neighbor)) {
                        if (isPassable?.Invoke((T) neighbor) == false) {
                            continue;
                        }
                        float newDist = distances[current] + link.Length;
                        if (!distances.ContainsKey(neighbor) || newDist < distances[neighbor]) {
                            distances[neighbor] = newDist;
                            priorityQueue.Enqueue(neighbor, newDist);
                            cameFrom[neighbor] = current;
                        }
                    }
                }
            }

            return false;
        }

        public static List<SOLooks> LoadLookTemplates(SpaceObject ctx, string looksTypeId) {
            if (!looksByTypeCache.TryGetValue(looksTypeId, out var templates)) {
                var file = $"Config/Generation/SpaceObjectLooks/{looksTypeId}";
                templates = The.ModLoader.LoadObjects<SOLooks>(file);
                looksByTypeCache[looksTypeId] = templates;
                if (templates == null) {
                    D.Err("No looks for {0}! Create a file!", ctx, file);
                    return null;
                }
            }
            return templates;
        }
        public static void DeployReturnBeacon(GameState s, int tileDefIdH) {
            var beaconSO = s.Universe.CreateObject();
            beaconSO.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.ReturnBeacon));
            A.Player.Parent.AddChild(beaconSO);
            Vector2 beaconPos;
            var playerLoc = A.Player.Location;
            if (playerLoc.sqrMagnitude < 4) {
                beaconPos = playerLoc + Rng.UInsideUnitCircle().normalized * Rng.URange(1f, 2f);
            } else {
                beaconPos = Vector2.MoveTowards(A.Player.Location, Consts.V2Zero, Rng.URange(1f, 2f));
            }
            beaconSO.Location = beaconPos;
            s.Sig.SpaceObjectAdded.Send(beaconSO);
            s.Sys.Universe.ShowInStarmap(beaconSO);
            var msg = T.ObjectDeployed_V1.F(Texts.LinkToCodex(tileDefIdH));
            msg = $"{msg}: {A.Player.Parent.ParentPath(fromPlayer: false)}";
            s.Sig.ShowFanfare.Send(msg);
            s.Sys.Log.AddLine(msg);
        }

        public static T RandomAtDist<T>(T start, int minDist, Rng rng,
                bool canBeVisited = true,
                float minDiff = 0f, float maxDiff = 1f,
                bool skipRegionsWithoutRelay = false,
                bool isFallback = false) where T : ISpaceEntity {
            if (minDist == 0) {
                return start;
            }
            if (skipRegionsWithoutRelay && start is not SpaceRegion) {
                skipRegionsWithoutRelay = false;
            }
            if (maxDiff <= 0f) {
                maxDiff = 1f;
            }
            var result = new List<T>();
            var visited = Caches.IntSet;
            visited.Clear();
            visited.Add(start.Id);
            var queue = new Stack<(int depth, T reg)>();
            queue.Push((depth: 0, reg: start));
            while (queue.TryPop(out var regionWithDepth)) {
                var depth = regionWithDepth.depth + 1;
                var region = regionWithDepth.reg;
                foreach (var link in region.Links) {
                    if (skipRegionsWithoutRelay && SpaceLink.IsIsolatedRegion(link)) {
                        continue;
                    }
                    T n = (T) link.OtherSideOf(region);
                    if (visited.Add(n.Id)) {
                        queue.Push((depth, n));
                        var diff = n.Biome.Difficulty;
                        if (minDiff > diff || maxDiff < diff) {
                            continue;
                        }
                        if (depth >= minDist) {
                            if (depth > minDist && result.Count > 0) {
                                // already have closer candidates, no need to overdo it
                                continue;
                            }
                            if (!canBeVisited && n.SO.IsVisited) {
                                continue;
                            }
                            result.Add(n);
                            if (result.Count > 10) {
                                // enough, let's cut the loop and give a result
                                return rng.From(result.OrderBy(n => n.Id).ToArray());
                            }
                        }
                    }
                }
            }
            if (result.Count == 0) {
                if (isFallback) {
                    D.Err("Failed finding space entity at dist: {0}, falling back to start: {1}",
                        minDist, start);
                    return start;
                }
                if (skipRegionsWithoutRelay) {
                    // try without relay
                    return RandomAtDist(start, minDist, rng, canBeVisited, minDiff, maxDiff,
                        false, isFallback: false);
                } else {
                    // final fallback! reduce min dist and increase diff range
                    return RandomAtDist(start, Mathf.Max(minDist - 1, 0), rng,
                        canBeVisited,
                        Mathf.Max(minDiff - 0.1f, 0f),
                        Mathf.Min(maxDiff + 0.1f, 1f),
                        false, isFallback: true);
                }
            }
            return rng.From(result.OrderBy(n => n.Id).ToArray());
        }
        public static void EnsureKnowledgeOfPOI(SpaceObject so, int type)  {
            if (so.Knowledge.POIs == null) {
                so.Knowledge.POIs = new();
            }
            if (!so.Knowledge.POIs.ContainsKey(type)) {
                so.Knowledge.POIs.Add(type, 1);
            }
        }

        public static SpaceObject SpawnAdditionalPOI(SpaceObject so, string poiCfgId, Rng rng) {
            if (so.Sector == null) {
                return null;
            }
            var poiCfg = POIConfig.Get(poiCfgId);
            if (!so.Sector.Biome.POIs.TryGetValue(poiCfg, out var cnt)) {
                cnt = 0;
            }
            so.Sector.Biome.POIs[poiCfg] = cnt + 1;
            SpaceObject target = null;

            var soType = SpaceObjectType.Find(poiCfg.Type);
            if (so.HasChildren) {
                var gen = GenUniverse.FindGenerator(poiCfg.Type);
                var child = gen.Generate(so.S.Universe, UniverseLayer.SectorContents, soType, so);
                so.S.Sys.Scan.FindLocationForNewResource(child, so, rng, 10);
                if (soType.IdH == SpaceObjectTypeIdH.Port && !gen.ProducesShip) {
                    // D.Warn("Port generated at {0}", parent);
                    EnsurePortFaction(so, child, rng);
                } else if (soType.IdH == SpaceObjectTypeIdH.PlanetPOI) {
                    child.UpdateName(T.Unknown);
                }
                if (!gen.ProducesShip) {
                    so.AddChild(child);
                }
                so.S.Sig.SpaceObjectAdded.Send(child);
                target = child;
            } else {
                // if we set isEntering to false, the sector may not necessarily be generated!
                if (!so.S.Sys.Universe.GenerateSector(so, isEntering: true, false)) {
                    throw new Exception($"Failed generating sector: {so}");
                }
                for (int i = so.Children.Count - 1; i >= 0; i--) {
                    SpaceObject child = so.Children[i];
                    if (child.Type.IdH == soType.IdH) {
                        target = child;
                        break;
                    }
                }
                // not every sector generator (e.g. AsteroidZone) assigns Port faction itself
                if (target != null && soType.IdH == SpaceObjectTypeIdH.Port && target.Faction == null) {
                    EnsurePortFaction(so, target, rng);
                }
            }
            EnsureKnowledgeOfPOI(so, soType.IdH);
            return target;
        }

        /// <summary>
        /// Makes sure a Port (and its host sector) has a faction assigned, picking a weighted
        /// random faction from the sector's biome if none is set yet.
        /// </summary>
        public static void EnsurePortFaction(SpaceObject so, SpaceObject child, Rng rng) {
            var fac = so.Faction;
            if (fac == null) {
                D.Err("Trying to spawn a port on a planet without faction! {0}", so);
                if (!rng.TryWeightedFrom(so.Sector.Biome.FactionsByDominance, out fac)) {
                    fac = FactionDef.Get(FactionId.Adventurer);
                }
                so.SetFaction(fac);
            }
            child.SetFaction(fac);
            child.Data.SetString(SpaceObjectLooks.VarSprite, fac.FlagIcon);
        }

        public static bool IsQuestTarget(SpaceObject so) {
            return so.S.Query.GetQuestStepTargets.Ask(so).Length > 0;
        }

        public static IQuestProvider QuestProviderFor(SpaceObject so) {
            if (so == null) { return null; }
            if (so.IsShip) {
                return so.Ship;
            }
            return so;
        }
        public static void ScanWithRadar(GameState s, SpaceObject to, bool hasRadar,
                bool rebuildStarmap = false) {
            // scan the current sector
            s.Sys.Universe.GenerateSector(to, false);
            if (hasRadar) {
                ShipSys.FullScan(to);
            }
            ScanSurroundingsWithRadar(s, to, hasRadar, rebuildStarmap);
        }

        private static void ScanSurroundingsWithRadar(GameState s, SpaceObject to, bool hasRadar, bool rebuild) {
            s.Sig.SpaceObjectChanged.Send(to);
            if (hasRadar) {
                ScanNeighbors(s, to, hasRadar: hasRadar);
            }
            if (rebuild && UIShowing.Starmap) {
                s.UIQuery.RebuildStarmap.Ask(false, false);
            }
        }
        public static string CurrentStarmapLayer(GameState s, bool forHyperspace) {
            var ctx = A.StarmapCtx;
            if (ctx.Layer != null) {
                return AdjustedLayer(ctx.Layer, forHyperspace);
            }
            bool isInHyperspace = s.Universe.HyperspaceLocation != null;
            SpaceObject parent;
            if (isInHyperspace) {
                parent = s.Universe.HyperspaceLocation.Parent;
            } else {
                parent = s.Universe.Player.Parent;
            }
            return AdjustedLayer(parent.Layer, forHyperspace);
        }
        private static string AdjustedLayer(string hsLayer, bool forHyperspace) {
            if (forHyperspace && hsLayer == UniverseLayer.Sector) {
                // we are jumping out of the sector, so the actual hsLayer is Region
                hsLayer = UniverseLayer.Region;
            }
            return hsLayer;
        }
        public static void ValidateLocation(GameState S) {
            if (!A.IsInHyperspace && S.Universe.Player.Parent.Layer != UniverseLayer.Sector) {
                D.Err("Player not in hyperspace but parent not a sector! {0}. HS loc: {1}",
                    S.Universe.Player.Parent, S.Universe.HyperspaceLocation);
                if (S.Universe.HyperspaceLocation != null) {
                    if (S.Sys.Ship.TryEnterObject(S.Universe.HyperspaceLocation,
                            S.Universe.Player.Parent)) {
                        D.Err("Fixed by moving player into HS loc");
                    }
                } else {
                    if (S.Sys.Ship.TryEnterObject(S.Universe.Player.Parent.Children?[0],
                            S.Universe.Player.Parent)) {
                        D.Err("Fixed by moving player into child");
                    }
                }
            }
        }

        public static void ScanNeighbors(GameState s, SpaceObject to, bool hasRadar) {
            ISpaceEntity entity = to.Sector;
            entity ??= to.Region;
            if (entity != null) {
                // scan surroundings
                foreach (var link in entity.Links) {
                    var other = link.OtherSideOf(entity);
                    s.Sys.Universe.GenerateSector(other.SO, false);
                    if (hasRadar) {
                        ShipSys.FullScan(other.SO);
                    }
                    s.Sig.SpaceObjectChanged.Send(other.SO);
                }
            }
        }
        /// <summary>
        /// Runs before the abandon warning in every hyperjump entry point: a ship that cannot
        /// be made to fly any time soon is told why, and nothing is queued or confirmed.
        /// </summary>
        public static bool CheckCanQueueHyperjump(GameState s, string hsLayer) {
            if (s.Sys.ShipNav.CanQueueHyperjump(hsLayer, out var failReason)) {
                return true;
            }
            WPopup.Warning(T.OperationFailed, null, $"{T.EnterHyperspaceFailed}:{failReason}");
            return false;
        }

        public static void CheckAbandonDerelicts(Action onAbandon, bool skipCheckIfHasQueue = true,
                bool isHyperjump = true) {
            if (skipCheckIfHasQueue) {
                var q = A.S.Query.GetHyperjumpQueue.Ask();
                if (q.Queue.Count > 0 || q.WarpTarget != null) {
                    onAbandon.Invoke();
                    return;
                }
            }
            // Hyperjumping destroys loose items and non-player beings on abandoned sections
            // too (see AbandonPreview), not just the sections themselves like in-sector travel.
            string derelictWarning;
            bool hasDerelicts = isHyperjump
                ? AbandonPreview.TryBuild(A.S, out derelictWarning)
                : A.S.Sys.Areas.HasDerelicts(out derelictWarning);
            var derelictSubtitle = isHyperjump ? T.HyperjumpAbandonWarning : T.DerelictAbandonWarning;
            var pendingOrders = A.S.Query.GetOnlineOrders.Ask(false);
            bool hasPendingOrders = !Arrays.IsEmpty(pendingOrders);
            if (!hasDerelicts && !hasPendingOrders) {
                onAbandon.Invoke();
                return;
            }
            string subtitle;
            string text;
            if (hasDerelicts && hasPendingOrders) {
                subtitle = derelictSubtitle;
                text = $"{derelictWarning}\n\n{T.OnlineOrderHyperjumpWarning}";
            } else if (hasDerelicts) {
                subtitle = derelictSubtitle;
                text = derelictWarning;
            } else {
                subtitle = T.OnlineOrderHyperjumpWarning;
                text = null;
            }
            WPopup.Create(false, IconId.CWarning, T.Warning, subtitle, text)
                .WithAction(T.Confirm, onAbandon)
                .WithAction2(T.Cancel, null)
                    .Spawn();
        }

        // The confirm is non-modal, so RMB-ing the starmap again while it is open starts another
        // travel confirm. Unique() collapses that into a single popup instead of stacking them,
        // since the older duplicate is despawned automatically once the newer one spawns with
        // the same id.
        private const string TravelAbandonConfirmId = "TravelAbandonConfirm";

        /// <summary>
        /// Shared gate for every "fly the player ship somewhere" entry point (TravelTo action,
        /// AttackShip, JoinShipBattle, ...). Flight abandons derelict sections mid-flight, so a
        /// raw ShipSys.SetDestination call bypasses the warning and abandons silently - always
        /// route the flight through this.
        /// </summary>
        public static void CheckAbandonOnTravel(GameState s, Action onTravel) {
            if (s.Sys.Areas.HasDerelicts(out var abandonWarning)) {
                WPopup.Create(false, IconId.CWarning, T.Warning, T.DerelictAbandonWarning, abandonWarning)
                    .WithAction2(T.Cancel, null)
                    .WithAction(T.Confirm, onTravel)
                    .Unique(TravelAbandonConfirmId)
                    .Spawn(true);
            } else {
                onTravel();
            }
        }

    }
}
