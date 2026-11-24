using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Modifiers;
using Game.Systems.Space;
using Game.Utils;
using Kl.Algos;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen.Space {
    public abstract class GenSpaceObject {
        public abstract string TypeId { get; }
        public virtual bool ProducesShip => false;
        public virtual SpaceObject Generate(Universe u, string layer,
                SpaceObjectType type, SpaceObject parent) {
            if (type == null) {
                D.Err("Type cannot be null when generating a space object! Layer {0}", layer);
                return null;
            }
            if (layer == null) {
                D.Err("Layer cannot be null when generating a space object! Type {0}", type);
                return null;
            }

            var ob = u.CreateObject();
            var rng = new Rng(Hashes.Quick(ob.Seed, Primes.Prime072));
            ob.Type = type;
            ob.Layer = layer;
            if (type.SizeMax > 0) {
                ob.Size = rng.Range(type.SizeMin, type.SizeMax);
            }
            if (ob.Size < 0.000001f) {
                ob.Size = 1f;
            }

            Update(u, ob);
            return ob;
        }

        public const float SpaceStationMinDist = 20f;
        public const float SpaceStationMaxDist = 50f;
        // Closer than this, surface POI icons and starmap labels stack on top of each other
        public const float MinSurfacePOIDist = 3f;
        private const int CloseupPointAttempts = 10;

        protected virtual Vector2[] GenerateChildPoints(Universe u, SpaceObject ob, int childrenCount) {
            throw new NotImplementedException(
                "Need to implement GenerateChildPoints if you want to GenerateSubobjects!");
        }
        protected virtual Curve curve1to5 => new float[] {
            0f, 1f,
            0.25f, 1f,
            0.5f, 2f,
            0.7f, 3f,
            1f, 5f
        };
        protected virtual Curve curve1to_10 => new float[] {
            0f, 1.1f,
            0.25f, 2f,
            0.5f, 4f,
            0.7f, 6f,
            1f, 10f
        };
        public Vector2[] GenerateSpiralPoints(Universe u, SpaceObject ob,
                int childrenCount, float initialR, Rng rng = null) {
            // We already have a blackhole in center, let's just return the rest
            // of the spiral
            var res = new Vector2[childrenCount];
            var t = ob.Type;
            var radius = t.MinDistanceBetweenSubObjects;

            rng ??= new Rng(Hashes.Quick(ob.Seed, Primes.Prime034));

            // Initial distance from the center:
            var r = initialR;
            for (int i = 0; i < childrenCount; i++) {
                var vec = rng.InsideUnitCircle().normalized;
                float v = i / (float) childrenCount;
                var rd = radius * rng.Range(curve1to5.Evaluate(v), curve1to_10.Evaluate(v));
                r += rd;
                res[i] = vec * r;
            }
            return res;
        }
        public abstract void GenerateSubObjects(Universe u, SpaceObject parent);

        public virtual void GenerateSubObjectsAt(Universe u, SpaceObject parent,
                Vector2[] points, Dictionary<SpaceObjectType, float> possibilities,
                List<SpaceObject> generatedObjects) {
            if (points.Length == 0) {
                D.Err("Trying to generate 0 sub objects for {0}", parent);
                return;
            }

            if (possibilities.Count == 0) {
                D.Err("Nothing can be generated underneath object with type {0}. Check ParentIds of other objects",
                    parent.Type.Id);
                return;
            }

            if (Arrays.IsEmpty(points)) {
                D.Err("Child points are empty, can't generate children for: {0}", parent);
            } else {
                var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime037));
                for (int i = 0; i < points.Length; i++) {
                    var type = rng.WeightedFrom(possibilities);
                    // D.Warn("Generating: {0}", type);
                    var gen = GenUniverse.FindGenerator(type.Id);
                    var child = gen.Generate(u, UniverseLayer.SectorContents, type, parent);
                    if (child.Ship != null) {
                        // Keeps the ship's local position in sync and marks it placed,
                        // so entering the sector doesn't randomize it
                        child.Ship.PlaceInSector(points[i]);
                    } else {
                        child.Location = points[i];
                    }
                    if (!gen.ProducesShip) {
                        if (!parent.AddChild(child)) {
                            D.Err("Failed to add child {0} to parent {1}. Gen {2}. Ship {3}",
                                child, parent, gen, child.Ship);
                        }
                    }
                    generatedObjects?.Add(child);
                }
            }
        }

        /// <summary>
        /// Universal method for generating SpaceObject.POIs for any
        /// location (planets, asteroid zones)
        /// </summary>
        /// <param name="u"></param>
        /// <param name="parent">sector, not close-up</param>
        /// <param name="closeupType">Only for planets</param>
        /// <param name="satellites">Planet moons, or asteroids</param>
        /// <param name="generatedObjects">If not null, will be filled with generated objects</param>
        public virtual void GeneratePOIs(Universe u, SpaceObject parent,
                SpaceObjectType closeupType, List<SpaceObject> satellites,
                List<SpaceObject> generatedObjects) {

            Vector2[] surfacePoints = null;
            Vector2[] spacePoints = null;

            // first we figure out how many points we'll need on surface and in space
            // deposits will always be on surface
            int poisOnSurface = parent.Mats?.Count ?? 0;
            int poisInSpace = 0;
            if (parent.POIs != null) {
                foreach (var p in parent.POIs) {
                    var poiType = SpaceObjectType.Find(p.Key);
                    if (poiType == null) {
                        D.Err("Unknown POI type! {0}", SpaceObjectTypeIdH.ResolveName(p.Key));
                        continue;
                    }
                    var cnt = p.Value;
                    if (poiType.IsOnSurface) {
                        poisOnSurface += cnt;
                    } else {
                        poisInSpace += cnt;
                    }
                }
            }
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime031));

            if (poisOnSurface > 0) {
                if (closeupType != null) {
                    // planet
                    var minDist = 1f;
                    var maxDist = 10f;
                    int numChildren = poisOnSurface;
                    var step = (maxDist - minDist) / numChildren;
                    surfacePoints = GeneratePointsOnCloseup(u, parent, numChildren,
                        minDist, maxDist, step);

                    // // Place a few inner points on top of satellites
                    // for (int i = satellites.Count - 1; i >= 0; i--) {
                    //     SpaceObject sat = satellites[i];
                    //     if (i < surfacePoints.Length && rng.Chance(0.5f)) {
                    //         surfacePoints[i] = sat.Location +
                    //             rng.InsideUnitCircle().normalized * (sat.Size * 0.25f);
                    //     }
                    // }
                } else {
                    if (satellites.Count == 0) {
                        D.Err("Cannot generate POIs - no close-up and no sattelites! {0}", parent);
                        return;
                    }
                    surfacePoints = GeneratePointsOnSatellites(satellites, poisOnSurface, rng);
                }
                D.Ass(surfacePoints.Length == poisOnSurface, "Pois on surface count mismatch! Wanted {0} Got {1}",
                    poisOnSurface, surfacePoints.Length);
                Arrays.Shuffle(rng, surfacePoints);
            }

            if (poisInSpace > 0) {
                spacePoints = GenerateSpiralPoints(u, parent, poisInSpace, parent.Type.MinExitDist, rng);
                D.Ass(spacePoints.Length == poisInSpace, "Pois in space count mismatch! Wanted {0} Got {1}",
                    poisInSpace, spacePoints.Length);
                Arrays.Shuffle(rng, spacePoints);
            }

            int nextSpacePoint = 0;
            int nextSurfPoint = 0;
            var depositType = SpaceObjectType.Find(SpaceObjectTypeIdH.PlanetPOI);
            foreach (var m in parent.Mats) {
                var point = surfacePoints[nextSurfPoint++];
                var gen = GenUniverse.FindGenerator(depositType.Id);
                var child = gen.Generate(u, UniverseLayer.SectorContents,
                    depositType, parent);
                child.Location = point;
                parent.AddChild(child);
                child.UpdateName(T.Unknown);
                generatedObjects?.Add(child);
            }
            if (parent.POIs != null) {
                foreach (var p in parent.POIs) {
                    var poiType = SpaceObjectType.Find(p.Key);
                    // already errored before
                    if (poiType == null) { continue; }
                    var cnt = p.Value;
                    for (int i = 0; i < cnt; i++) {
                        Vector2 point;
                        if (poiType.IsOnSurface) {
                            point = surfacePoints[nextSurfPoint++];
                        } else {
                            point = spacePoints[nextSpacePoint++];
                        }
                        var gen = GenUniverse.FindGenerator(poiType.Id);
                        var child = gen.Generate(u, UniverseLayer.SectorContents, poiType, parent);
                        if (poiType.IdH == SpaceObjectTypeIdH.Port && !gen.ProducesShip) {
                            // D.Warn("Port generated at {0}", parent);
                        } else if (poiType.IdH == SpaceObjectTypeIdH.PlanetPOI) {
                            child.UpdateName(T.Unknown);
                        }
                        if (child.Ship != null) {
                            child.Ship.PlaceInSector(point);
                        } else {
                            child.Location = point;
                        }
                        if (!gen.ProducesShip) {
                            parent.AddChild(child);
                        }
                        generatedObjects?.Add(child);
                    }
                }
            }

            // after generating, remove POIs that are ships
            RemoveShipPOIsFrom(parent, parent.POIs);
            if (parent.HasKnowledge) {
                RemoveShipPOIsFrom(parent, parent.Knowledge.POIs);
            }

            // must call this afterwards in the caller!
            // possibly after assigning faction infra
            // u.S.Sys.Planets.AssignDepositsFor(parent);
        }

        private static void RemoveShipPOIsFrom(SpaceObject parent, Dictionary<int, int> pois) {
            if (Arrays.IsEmpty(pois)) { return; }
            Caches.IntList.Clear();
            foreach (var poiKey in pois.Keys) {
                var poi = SpaceObjectType.Find(poiKey);
                if (poi == null) {
                    D.Err("Unknown POI type! {0}", SpaceObjectTypeIdH.ResolveName(poiKey));
                    continue;
                }
                var cfg = POIConfig.TryGet(poi.Id);
                if (cfg == null) { continue; }
                if (cfg.ProducesShip) {
                    Caches.IntList.Add(poiKey);
                    // D.Warn("Removing ship POI {0} from {1}", poi, parent);
                }
            }
            foreach (var id in Caches.IntList) {
                pois.Remove(id);
            }
        }

        // One POI per satellite while there are enough of them: satellites are spaced apart by
        // generation, but POIs sharing one sit on a small ring around it and would stack.
        // The generators add a satellite per POI, so sharing is only a fallback.
        private static Vector2[] GeneratePointsOnSatellites(List<SpaceObject> satellites,
                int count, Rng rng) {
            // satellites can be a shared cache, don't shuffle it in place
            var hosts = satellites.ToArray();
            Arrays.Shuffle(rng, hosts);
            var baseAngles = new float[hosts.Length];
            var res = new Vector2[count];
            for (int i = 0; i < count; i++) {
                int hostIdx = i % hosts.Length;
                int slot = i / hosts.Length;
                if (slot == 0) {
                    baseAngles[hostIdx] = rng.Range(0f, Maths.PIx2);
                }
                int slots = count / hosts.Length + (hostIdx < count % hosts.Length ? 1 : 0);
                float angle = baseAngles[hostIdx] + slot * Maths.PIx2 / slots;
                var host = hosts[hostIdx];
                res[i] = host.Location
                    + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (host.Size * 0.25f);
            }
            return res;
        }

        private static float SqrDistToNearest(Vector2 point, Vector2[] points, int count) {
            float min = float.MaxValue;
            for (int i = 0; i < count; i++) {
                float d = (points[i] - point).sqrMagnitude;
                if (d < min) {
                    min = d;
                }
            }
            return min;
        }

        private Vector2[] GeneratePointsOnCloseup(Universe u, SpaceObject obj,
                int count, float minDistFromCenter, float maxDistFromCenter,
                float distStep, float variance = 0.25f) {
            // Can't do this because POIs get generated outside
            //return GenerateSpiralPoints(u, obj, count, minDistFromCenter);

            // We already have a star in center, let's just return the rest
            // of the spiral
            var res = new Vector2[count];

            var rng = new Rng(Hashes.Quick(obj.Seed, Primes.Prime026));

            var ox = rng.Range(1f - variance, 1f + variance) * rng.OneOf(-1, 1);
            var oy = rng.Range(1f - variance, 1f + variance) * rng.OneOf(-1, 1);
            var off = new Vector2(ox, oy);
            int childrenCount = 0;
            var curDist = minDistFromCenter;
            int curAngleIdx = 0;
            const float minSqrDist = MinSurfacePOIDist * MinSurfacePOIDist;
            while (childrenCount < count) {
                // Re-roll the angle and jitter until the point clears its neighbours; on a
                // crowded closeup keep the roll that is farthest from them
                Vector2 best = default;
                float bestSqrDist = -1f;
                for (int attempt = 0; attempt < CloseupPointAttempts; attempt++) {
                    float angle = curAngleIdx++ * rng.Range(0.5f, 3f);
                    float x = Mathf.Cos(angle);
                    float y = Mathf.Sin(angle);
                    var vec = new Vector2(x, y).normalized * off * curDist;
                    var candidate = vec + rng.InsideUnitCircle().normalized
                        * rng.Range(1f - variance, 1f + variance);
                    float sqrDist = SqrDistToNearest(candidate, res, childrenCount);
                    if (sqrDist > bestSqrDist) {
                        best = candidate;
                        bestSqrDist = sqrDist;
                    }
                    if (sqrDist >= minSqrDist) {
                        break;
                    }
                }
                res[childrenCount++] = best;
                curDist += distStep;
                if (curDist > maxDistFromCenter) {
                    curDist = minDistFromCenter;
                }
            }

            return res;
        }

        public virtual void Update(Universe u, SpaceObject so) { }
    }
}
