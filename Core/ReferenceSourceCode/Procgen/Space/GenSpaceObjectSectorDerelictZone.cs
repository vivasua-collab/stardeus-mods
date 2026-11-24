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
    public class GenSpaceObjectSectorDerelictZone: GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.DerelictZone;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.DerelictZone,
                new GenSpaceObjectSectorDerelictZone());
        }

        private static Curve numDerelictsCurve = new float[] {
            0f, 1f,
            0.9f, 1f,
            0.91f, 2f,
            1.0f, 2f,
        };

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime027));

            var numDerelicts = rng.IntCurve(numDerelictsCurve);
            if (numDerelicts < 1) {
                numDerelicts = 1;
            }

            var points = GenerateSpiralPoints(u, parent, numDerelicts, 10f);

            foreach (var point in points) {
                var derelict = u.S.Query.SpawnShipAt.Ask(parent);
                derelict.TurnIntoDerelict(null);
                derelict.PlaceInSector(point);
            }
        }
    }
}
