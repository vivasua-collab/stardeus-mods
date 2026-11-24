using Game.Constants;
using Game.Data.Space;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Space;
using System.Collections.Generic;
using Game.Data;
using Game.CodeGen;

using Game.Utils;
namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorSpaceEvent : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.SpaceEvent;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.SpaceEvent,
                new GenSpaceObjectSectorSpaceEvent());
        }

        private static Curve numAsteroidsCurve = new float[] {
            0f, 2f,
            0.2f, 3f,
            0.5f, 4f,
            0.9f, 5f,
            1.0f, 6f,
        };

        protected override Vector2[] GenerateChildPoints(Universe u, SpaceObject ob, int childrenCount) {
            return GenUniverseUtils.GeneratePoissonDiscChildPoints(u, ob, childrenCount, this);
        }
        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime027));

            parent.Mats = new List<MatType>();
            parent.Mats.Add(MatType.FastGet(MatIdH.Rock));
            parent.Sector.Biome.Deposits.Add(DepositConfig.Get(MatId.Rock), 1);
            int numChildren = 1;
            if (rng.Chance(0.5f)) {
                var evt = POIConfig.Get(rng.OneOf("Event", "Signal"));
                parent.Sector.Biome.POIs.Add(evt, 1);
                var type = SpaceObjectType.Find(evt.Type);
                parent.ChangePOIs(type.IdH, 1, syncKnowledge: true);
                numChildren++;
            }

            var num = rng.IntCurve(numAsteroidsCurve);
            num += numChildren;

            var ssPoints = GenerateChildPoints(u, parent, num);
            var possibilities = new Dictionary<SpaceObjectType, float>() {
                { SpaceObjectType.Find(SpaceObjectTypeId.Asteroid), 1f }
            };
            Caches.SpaceObjectList.Clear();
            GenerateSubObjectsAt(u, parent, ssPoints, possibilities, Caches.SpaceObjectList);
            foreach (var child in parent.Children) {
                if (child.Type.IdH == SpaceObjectTypeIdH.Asteroid) {
                    child.UpdateName(T.Asteroid);
                }
            }

            GeneratePOIs(u, parent, null, Caches.SpaceObjectList, null);

            u.S.Sys.Planets.AssignDepositsFor(parent);
        }
    }
}
