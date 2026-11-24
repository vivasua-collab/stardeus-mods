#if !PRODUCTION_BUILD
// #define VIZ_SPACE_GEN
#define MAP_REPORT
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Naming;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Kl.Algos;
using KL.Algos;
using KL.Randomness;
using KL.Text;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using MessagePack;

namespace Game.Systems.Space {
    [MessagePackObject]
    public struct GenSpaceParams {
        [Key("DifficultyShift")] public float DifficultyShift;
        [Key("MapSizeMul")] public float MapSizeMul;
    }

    public sealed class GenSpace {
        private int seed;
        private GenSpaceBiome biomeGen;
        private GenSpaceParams genParams;
        private readonly GameState S;
        private readonly List<NameResult> regionNameResults = new();
        // Don't change this, it will change how many regions / sectors are created!
        private Vector2? _mapSize;
        private Vector2 mapSize => _mapSize ??= new Vector2(Tunables.UniverseWidth, Tunables.UniverseHeight);
        public GenSpace(GameState s, int seed) {
            S = s;
            this.seed = seed;
        }
        public SpaceMap Generate(GenSpaceParams genParams) {
            regionNameResults.Clear();
            this.genParams = genParams;
            var map = new SpaceMap();
            map.GenParams = genParams;
            biomeGen = new GenSpaceBiome();
            var rng = new Rng(seed);
            var exceptNames = new HashSet<int>();
            GenerateRegions(map, rng, exceptNames);
            GenerateSectors(map, rng, exceptNames);
            biomeGen.GenerateRegionBiomes(map, rng, mapSize, genParams);
            biomeGen.GenerateSectorBiomes(map, rng);
            biomeGen.AssignLeftovers(map, rng);

            ValidateHyperjumpRelays(map);

            #if MAP_REPORT
            PrintReport(map, seed);
            #endif
            Dictionary<MatType, int> numDeposits = new() {
                { MatType.FastGet(MatIdH.Edenium), 0 },
                { MatType.FastGet(MatIdH.Aerolith), 0 },
                { MatType.FastGet(MatIdH.Terranite), 0 },
                { MatType.FastGet(MatIdH.Hydronium), 0 },
                { MatType.FastGet(MatIdH.DarkMatter), 0 },
            };
            var deps = numDeposits.Keys.ToArray();

            foreach (var reg in map.Regions) {
                foreach (var sec in reg.Sectors) {
                    foreach (var dep in deps) {
                        if (sec.Biome.HasDepositsFor(dep)) {
                            numDeposits[dep]++;
                        }
                    }
                }
            }
            bool hasMissing = false;
            foreach (var dep in numDeposits) {
                if (dep.Value == 0) {
                    D.Err("Missing critical deposit! {0}. Space seed: {1}",
                        dep.Key, seed);
                    hasMissing = true;
                }
            }
            if (hasMissing) {
                WPopup.Create(true, IconId.CWarning, T.Warning, "This seed produced a map that lacks some essential resources. Please report a bug and then generate another map").Spawn();
            }
            return map;
        }

        private void ValidateHyperjumpRelays(SpaceMap map) {
            foreach (var reg in map.Regions) {
                var hasRelay = SOUtils.HasHyperjumpRelay(reg);
                if (!hasRelay) { continue; }
                bool hasNeighborWithRelay = false;
                foreach (var link in reg.Links) {
                    var n = link.OtherSideOf(reg);
                    if (SOUtils.HasHyperjumpRelay((SpaceRegion) n)) {
                        hasNeighborWithRelay = true;
                        break;
                    }
                }
                if (!hasNeighborWithRelay) {
                    D.Warn("Removing hyperjump relay from a region that has no neighbors with relays: {0}", reg);
                    foreach (var sec in reg.Sectors) {
                        if (sec.IsHyperjumpRelay) {
                            sec.Biome.SetTemplate(SectorTemplate.Get(SpaceEntityTemplateId.SpaceEvent));
                        }
                    }
                }
            }
        }

        public void PrintReport(SpaceMap map, int seed) {
            var mats = Caches.MatCounts;
            var effects = Caches.MapStringInt;
            var pois = new Dictionary<string, int>();
            var templates = new Dictionary<string, int>();
            mats.Clear();
            effects.Clear();
            foreach (var reg in map.Regions) {
                foreach (var sec in reg.Sectors) {
                    if (!templates.TryGetValue(sec.Biome.Template.Id, out var tcnt)) {
                        tcnt = 0;
                    }
                    templates[sec.Biome.Template.Id] = tcnt + 1;
                    foreach (var dep in sec.Biome.Deposits) {
                        if (!mats.TryGetValue(dep.Key.Mat, out var cnt)) {
                            cnt = 0;
                        }
                        mats[dep.Key.Mat] = cnt + dep.Value;
                    }
                    foreach (var eff in sec.Biome.Effects) {
                        if (!effects.TryGetValue(eff.Key.Id, out var cnt)) {
                            cnt = 0;
                        }
                        effects[eff.Key.Id] = cnt + eff.Value;
                    }
                    foreach (var poi in sec.Biome.POIs) {
                        if (!pois.TryGetValue(poi.Key.Id, out var cnt)) {
                            cnt = 0;
                        }
                        pois[poi.Key.Id] = cnt + poi.Value;
                    }
                }
            }
            D.Warn("Map report: Seed: {0} Mats: {1}, Effects: {2}, POIs: {3}. Templates: {4}", seed,
                string.Join(", ", mats.OrderByDescending(m => m.Value).Select(m => $"{m.Key.Id} x{m.Value}")),
                string.Join(", ", effects.OrderByDescending(m => m.Value).Select(m => $"{m.Key} x{m.Value}")),
                string.Join(", ", pois.OrderByDescending(m => m.Value).Select(m => $"{m.Key} x{m.Value}")),
                string.Join(", ", templates.OrderByDescending(m => m.Value).Select(m => $"{m.Key} x{m.Value}"))
            );
        }

        private void GenerateSectors(SpaceMap map, Rng rng, HashSet<int> exceptNames) {
            var nextSectorId = 1;
            var sectorLinks = new HashSet<SpaceLink>();
            var regionSize = GetRegionSize();
            foreach (var reg in map.Regions) {
                nextSectorId = CreateSectorsFor(nextSectorId, reg, rng, exceptNames,
                    regionSize, 1f);
                sectorLinks.Clear();
                var linkables = reg.Sectors;
                GenerateLinks(reg.Name, rng, linkables, sectorLinks, 2, 4);
            }
            D.Warn("Total regions: {0}. Sectors: {1}", map.Regions.Count, nextSectorId - 1);
        }

        private int CreateSectorsFor(int nextSectorId, SpaceRegion reg, Rng rng,
                HashSet<int> exceptNames, Vector2 regionSize, float sectorSizeScale) {
            int attempts = 0;
            List<Vector2> points = null;
            while (attempts++ < 200) {
                var pointDistFrom = rng.Range(1f, 2f) * sectorSizeScale;
                var pointDistTo = rng.Range(5f, 8f) * sectorSizeScale;
                points = PoissonSpajusDiscSampling.GeneratePoints(
                    pointDistFrom, pointDistTo, regionSize, rng);
                if (points.Count >= 12 && points.Count <= 25) {
                    break;
                }
            }
            if (points.Count < 12) {
                D.Err("Failed to generate enough sector points ({0}) in {1} attempts in region: {1}",
                    points.Count, attempts, reg.Name);
            }
            // D.Warn("Got region points: {0} in {1} attempts", points.Count, attempts);
            var nameRng = rng.Fork();
            reg.Sectors = new();
            foreach (var point in points) {
                var sec = new SpaceSector();
                sec.Seed = rng.Int;
                sec.Id = nextSectorId++;
                sec.Region = reg;
                sec.Position = point - regionSize * 0.5f;
                sec.Biome = new SpaceBiome();
                sec.Biome.Type = SpaceBiomeType.Sector;
                sec.Biome.Difficulty = -1;
                    // Mathf.Clamp01(reg.Biome.Difficulty + rng.RangeSmooth(-0.15f, 0.15f));
                var name = S.Query.GenerateNameExcept.Ask(
                        NameRequest.ForSpaceMap(SpaceMapObjectType.Sector, nameRng),
                        exceptNames);
                sec.Name = name.Name;
                reg.Sectors.Add(sec);
            }
            return nextSectorId;
        }

        private Vector2 GetRegionSize() {
            return new Vector2(
                Tunables.UniverseRegionWidth,
                Tunables.UniverseRegionHeight);
        }


        private void GenerateRegions(SpaceMap map, Rng rng, HashSet<int> exceptNames) {
            D.Warn("Generating regions with seed: {0}", rng.State);
            map.Regions = new();
            var mapSizeMul = genParams.MapSizeMul;
            if (mapSizeMul <= 0f) {
                mapSizeMul = 1f;
            }
            var mapSize = this.mapSize * mapSizeMul;
            var points = PoissonSpajusDiscSampling.GeneratePoints(0.85f, 5f, mapSize, rng);
            // D.Warn("Got points: {0}", points.Count);

            var nextRegionId = 1;
            var nameRng = new Rng(Hashes.Quick(Primes.Prime093, seed));
            foreach (var point in points) {
                var region = new SpaceRegion();
                region.Id = nextRegionId++;
                region.Seed = rng.Int;
                var name = S.Query.GenerateNameExcept.Ask(
                        NameRequest.ForSpaceMap(SpaceMapObjectType.Region, nameRng),
                        exceptNames);
                // name.Consume();
                region.Name = name.Name;
                region.Position = point - mapSize * 0.5f;
                region.Map = map;
                map.Regions.Add(region);
            }

            var linkables = map.Regions;
            var regionLinks = new HashSet<SpaceLink>();
            GenerateLinks("Regions", rng, linkables, regionLinks, connectFrom: 2, connectTo: 4);
        }

        private void GenerateLinks<T>(string ctx, Rng rng, List<T> linkables,
                HashSet<SpaceLink> existingLinks,
                int connectFrom, int connectTo) where T : ISpaceEntity {
            var neighborDistances = new Dictionary<T, float>();
            var linkAngles = new List<float>();
            foreach (T linkable in linkables) {
                neighborDistances.Clear();
                foreach (T l in linkables) {
                    if (l.Id == linkable.Id) { continue; }
                    neighborDistances.Add(l, Vectors.SqrDistance(linkable.Position, l.Position));
                }
                var willLinkTo = neighborDistances
                    .OrderBy(kv => kv.Value)
                    .Take(10)
                    .Select(kv => kv.Key)
                    .ToArray();
                linkAngles.Clear();

                int toConnect = rng.Range(connectFrom, connectTo + 1);
                foreach (var n in willLinkTo) {
                    var angle = Vectors.AnglesBetween(n.Position,
                        linkable.Position, linkable.Position + new Vector2(1f, 0));
                    bool skipAngle = false;
                    foreach (var a in linkAngles) {
                        if (Mathf.Abs(angle - a) < 45f) {
                            // skipping a shallow angle
                            skipAngle = true;
                            break;
                        }
                    }
                    if (skipAngle) { continue; }

                    linkAngles.Add(angle);
                    var link = new SpaceLink(linkable, n);
                    if (existingLinks.Add(link)) {
                        linkable.AddLink(link);
                        n.AddLink(link);
                    }

                    if (--toConnect == 0) {
                        break;
                    }
                }
            }
            RemoveCrossovers(existingLinks);
            RemoveSharpAngles(existingLinks);
            EnsureAllReachable(ctx, linkables);
        }

        private void EnsureAllReachable<T>(string ctx, List<T> linkables) where T : ISpaceEntity {
            // find connected
            var visited = new HashSet<T>();
            var islands = new List<List<T>>();
            foreach (var linkable in linkables) {
                if (visited.Contains(linkable)) { continue; }
                var island = new List<T>();
                DFS(linkable, visited, island);
                islands.Add(island);
            }

            // D.Warn("Islands in {0}: {1}", ctx, islands.Count);
            int attempts = 10;
            while (islands.Count > 1 && attempts-- > 0) {
                LinkIslands(islands[0], islands[1]);
                islands[0].AddRange(islands[1]);
                islands.RemoveAt(1);
            }
        }

        private void LinkIslands<T>(List<T> a, List<T> b) where T : ISpaceEntity {
            var minDist = float.MaxValue;
            (T, T) closestPair = default;
            foreach (var na in a) {
                foreach (var nb in b) {
                    var dist = Vectors.SqrDistance(na.Position, nb.Position);
                    if (dist < minDist) {
                        minDist = dist;
                        closestPair = (na, nb);
                    }
                }
            }

            var link = new SpaceLink(closestPair.Item1, closestPair.Item2);
            closestPair.Item1.AddLink(link);
            closestPair.Item2.AddLink(link);
            // D.Warn("Linked two islands via {0}", closestPair);
        }

        private static void DFS<T>(T linkable, HashSet<T> visited, List<T> island)
                where T : ISpaceEntity {
            visited.Add(linkable);
            island.Add(linkable);
            foreach (var link in linkable.Links) {
                var neighbor = (T) link.OtherSideOf(linkable);
                if (!visited.Contains(neighbor)) {
                    DFS(neighbor, visited, island);
                }
            }
        }

        private void RemoveSharpAngles(HashSet<SpaceLink> links) {
            bool mustRepeat = false;
            foreach (var link in links) {
                foreach (var n in link.From.Links) {
                    if (n == link) { continue; }
                    var angle = SpaceLink.AngleBetween(link, n);
                    if (angle < 25f) {
                        RemoveLongerLink(links, link, n);
                        mustRepeat = true;
                        break;
                    }
                }
                if (mustRepeat) {
                    break;
                }
            }
            if (mustRepeat) {
                RemoveSharpAngles(links);
            }
        }

        private void RemoveCrossovers(HashSet<SpaceLink> links) {
            bool mustRepeat = false;
            var count = links.Count * links.Count;
            if (count > 20000) {
                D.Warn("Will not remove crossovers - too many links! {0}", count);
                return;
            }
            foreach (var link in links) {
                foreach (var link2 in links) {
                    if (link == link2) { continue; }
                    if (link.HasCommonLinkable(link2)) { continue; }
                    var hasIntersection = Vectors.DoLineSegmentsIntersect(
                        link.FromWithOffset(Maths.Epsilon), link.ToWithOffset(Maths.Epsilon),
                        link2.FromWithOffset(Maths.Epsilon), link2.ToWithOffset(Maths.Epsilon));
                    if (hasIntersection) {
                        // D.Warn("Intersection: {0} and {1}", link, link2);
                        RemoveLongerLink(links, link, link2);
                        mustRepeat = true;
                        break;
                    }
                }
                if (mustRepeat) {
                    break;
                }
            }
            if (mustRepeat) {
                RemoveCrossovers(links);
            }
        }

        private static void RemoveLongerLink(HashSet<SpaceLink> links,
                SpaceLink a, SpaceLink b) {
            var longerLink = a.LengthSqr > b.LengthSqr ? a : b;
            if (!links.Remove(longerLink)) {
                D.Err("Failed removing longer link! {0} | {1}", longerLink, D.Dump(links));
            }
            longerLink.From.RemoveLink(longerLink);
            longerLink.To.RemoveLink(longerLink);
            // D.Warn("Removing longer link: {0}", longerLink);
        }

        public void Cleanup() {
            biomeGen.Cleanup();
        }

        public void Commit() {
            foreach (var name in regionNameResults) {
                name.Consume();
            }
        }
    }
}
