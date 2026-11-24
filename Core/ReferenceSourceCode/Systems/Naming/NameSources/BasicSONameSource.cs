using Game.CodeGen;
using Game.Constants;
using Game.Data.Space;
using Game.Procgen.Names;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class BasicSONameSource : NameSource {
        public const string SourceId = "BasicSO";

        [SelfInit]
        private static void Register() {
            Register(SourceId, new BasicSONameSource());
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
                var type = SpaceObjectType.Find(req.SubTypeId);
                res.SetName(GenNamesLegacy.For(req.SubTypeId).GenerateSO(type,
                    req.Rng, true,
                        Tunables.CustomSpaceObjectNameChance));
                if (!IsConsumed(res.Name)) {
                    return true;
                }
            }
            return false;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.SpaceObject;
        }
    }
}
