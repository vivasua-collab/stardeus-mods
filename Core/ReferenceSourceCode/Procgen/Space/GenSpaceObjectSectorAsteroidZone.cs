using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Space;

namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorAsteroidZone : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.AsteroidZone;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.AsteroidZone,
                new GenSpaceObjectSectorAsteroidZone());
        }

        private static Curve numAsteroidsCurve = new float[] {
            0f, 3f,
            0.2f, 4f,
            0.5f, 5f,
            0.9f, 7f,
            1.0f, 10f,
        };

        protected override Vector2[] GenerateChildPoints(Universe u, SpaceObject ob, int childrenCount) {
            return GenUniverseUtils.GeneratePoissonDiscChildPoints(u, ob, childrenCount, this);
        }

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime027));

            int numChildren = 0;
            if (parent.Mats == null) {
                parent.Mats = new List<MatType>();
                foreach (var cfg in parent.Sector.Biome.Deposits) {
                    for (int i = 0; i < cfg.Value; i++) {
                        parent.Mats.Add(cfg.Key.Mat);
                        numChildren++;
                    }
                }
                foreach (var cfg in parent.Sector.Biome.POIs) {
                    var type = SpaceObjectType.Find(cfg.Key.Type);
                    for (int i = 0; i < cfg.Value; i++) {
                        numChildren++;
                        parent.ChangePOIs(type.IdH, 1, syncKnowledge: true);
                    }
                }
            }

            if (numChildren == 0) {
                D.Err("No deposits or pois in asteroid zone!", parent);
            }

            var num = rng.IntCurve(numAsteroidsCurve);
            num += numChildren;

            var ssPoints = GenerateChildPoints(u, parent, num);
            var possibilities = new Dictionary<SpaceObjectType, float>() {
                { SpaceObjectType.Find(SpaceObjectTypeId.Asteroid), 1f }
            };
            Caches.SpaceObjectList.Clear();
            // Factions will be generated later
            GenerateSubObjectsAt(u, parent, ssPoints, possibilities, Caches.SpaceObjectList);
            foreach (var child in parent.Children) {
                if (child.Type.IdH == SpaceObjectTypeIdH.Asteroid) {
                    child.UpdateName(T.Asteroid);
                }
            }

            // Inner points of interest (on planet, or in satellites)
            // GeneratePointsOfInterest(u, parent, parent.Children, numChildren);
            GeneratePOIs(u, parent, null, Caches.SpaceObjectList, null);

            u.S.Sys.Planets.AssignDepositsFor(parent);
        }
    }
}
