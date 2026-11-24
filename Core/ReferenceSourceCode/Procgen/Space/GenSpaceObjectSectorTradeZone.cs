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
    public class GenSpaceObjectSectorTradeZone : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.TradeZone;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.TradeZone,
                new GenSpaceObjectSectorTradeZone());
        }

        private static Curve numStationsCurve = new float[] {
            0f, 2f,
            0.2f, 3f,
            0.5f, 4f,
            0.9f, 5f,
            1.0f, 8f,
        };

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime027));

            var numStations = rng.IntCurve(numStationsCurve);

            var ssPoints = GenerateStationPoints(parent, numStations, rng);
            var possibilities = new Dictionary<SpaceObjectType, float>() {
                { SpaceObjectType.Find(SpaceObjectTypeId.SpaceStation), 1f }
            };
            // Factions will be generated later
            var stations = new List<SpaceObject>();
            GenerateSubObjectsAt(u, parent, ssPoints, possibilities, stations);
            for (int i = 0; i < stations.Count; i++) {
                stations[i].Ship.TeleportToSectorLocation(ssPoints[i]);
            }

            // D.Warn("Done generating trade zone contents for {0}. ExitDist: {1}",
            //     parent, parent.ExitDist);
        }

        private static Vector2[] GenerateStationPoints(SpaceObject parent,
                int numStations, Rng rng) {
            var points = new Vector2[numStations];
            var stationsOnRing = numStations;
            var firstRingIndex = 0;
            if (numStations > 7) {
                points[0] = Vector2.zero;
                stationsOnRing--;
                firstRingIndex++;
            }

            var angleOffset = rng.Range(0f, Mathf.PI * 2f);
            var angleStep = Mathf.PI * 2f / stationsOnRing;
            var radius = parent.Type.MinExitDist;
            for (int i = 0; i < stationsOnRing; i++) {
                var angle = angleOffset + angleStep * i;
                points[firstRingIndex + i] = new Vector2(
                    Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }
    }
}
