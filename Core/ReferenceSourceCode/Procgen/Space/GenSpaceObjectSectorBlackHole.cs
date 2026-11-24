using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using Game.Systems.Space;

namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorBlackHole : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.BlackHole;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.BlackHole,
                new GenSpaceObjectSectorBlackHole());
        }

        public override void GenerateSubObjects(Universe u, SpaceObject obj) {
            // TODO - maybe add moons and stuff
            var holeInner = Generate(u, UniverseLayer.SectorContents, obj.Type, obj);
            obj.AddChild(holeInner);
        }

        protected override Vector2[] GenerateChildPoints(Universe u, SpaceObject ob, int count) {
            return null;
        }

        public static void BuildNetwork(GameState S) {
            var map = S.Query.GetSpaceMap.Ask();
            var holes = new List<SpaceObject>();
            foreach (var region in map.Regions) {
                foreach (var sector in region.Sectors) {
                    if (sector.Biome.Template.Id == "BlackHole") {
                        holes.Add(sector.SO);
                    }
                }
            }

            var orderRng = new Rng(Hashes.Quick(S.SeedSpace, Primes.Prime061));

            holes = holes.OrderBy(_ => orderRng.Value).ToList();

            for (int i = 0; i < holes.Count; i += 2) {
                SpaceObject hole = holes[i];
                if (i + 1 < holes.Count) {
                    var nextHole = holes[i+1];
                    nextHole.Data.SetInt("LeadsToId", hole.Id);
                    // 20% chance to lead into random hole instead
                    // if (orderRng.Chance(0.2f)) {
                    //     var altHole = orderRng.From(holes);
                    //     if (altHole != hole) {
                    //         nextHole = altHole;
                    //     }
                    // }
                    hole.Data.SetInt("LeadsToId", nextHole.Id);
                } else {
                    // D.Warn("Uneven amount of black holes!");
                    hole.Data.SetInt("LeadsToId", holes[0].Id);
                }
            }
        }
    }
}
