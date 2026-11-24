using Game.Data.Space;
using UnityEngine;
using Game.Constants;
using KL.Randomness;
using KL.Utils;
using Game.Data.Factions;
using Game.CodeGen;

using Game.Utils;
namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorHyperjumpRelay : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.HyperjumpRelay;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.HyperjumpRelay,
                new GenSpaceObjectSectorHyperjumpRelay());
        }

        public override void GenerateSubObjects(Universe u, SpaceObject obj) {
            var relayInner = Generate(u, UniverseLayer.SectorContents, obj.Type, obj);
            obj.AddChild(relayInner);
            AssignRelayFaction(relayInner);
        }

        public static void AssignRelayFaction(SpaceObject obj) {
            var sector = SOUtils.SectorFor(obj);
            var region = sector.Region;
            var rng = new Rng(Hashes.Quick(obj.Seed, Primes.Prime020));
            FactionDef fac;
            if (region.Biome.FactionsByDominance != null) {
                fac = rng.WeightedFrom(region.Biome.FactionsByDominance);
            } else {
                fac = FactionDef.FastGet(FactionIdH.Adventurer);
            }
            obj.SetFaction(fac);

        }

        protected override Vector2[] GenerateChildPoints(Universe u, SpaceObject ob, int count) {
            return null;
        }
    }
}
