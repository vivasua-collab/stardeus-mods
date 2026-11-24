using Game.Constants;
using Game.Data.Space;
using Game.Procgen.Names;
using Game.Systems.Space;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class SpaceMapNameSource : NameSource {
        public const string SourceId = "SpaceMap";

        [SelfInit]
        private static void Register() {
            Register(SourceId, new SpaceMapNameSource());
        }

        public override int PriorityFor(NameType type) {
            // last option
            return -10000;
        }

        public override float ChanceFor(NameRequest req) {
            // 100% chance
            return 1f;
        }

        private void ConsumeName(NameResult res) {
            Consume(res.Name);
        }

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            res = new NameResult(ConsumeName);
            int attempts = 50;
            while (attempts-- > 0) {
                string legacySOType = null;
                switch (req.SpaceType) {
                    case SpaceMapObjectType.Region: {
                        legacySOType = SpaceObjectTypeId.Region;
                        break;
                    }
                    case SpaceMapObjectType.Sector: {
                        legacySOType = SpaceObjectTypeId.Sector;
                        break;
                    }
                    default: {
                        legacySOType = SpaceObjectTypeId.Planet;
                        break;
                    }
                }
                var type = SpaceObjectType.Find(legacySOType);
                res.SetName(GenNamesLegacy.For(legacySOType).GenerateSO(
                    type, req.Rng, false,
                        Tunables.CustomSpaceObjectNameChance));
                if (!IsConsumed(res.Name)) {
                    return true;
                }
            }
            return false;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.SpaceMapObject;
        }
    }
}
