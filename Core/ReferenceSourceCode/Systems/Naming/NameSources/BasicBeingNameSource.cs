using Game.Constants;
using Game.Procgen.Names;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class BasicBeingNameSource : NameSource {
        public const string SourceId = "BasicBeing";

        [SelfInit]
        private static void Register() {
            Register(SourceId, new BasicBeingNameSource());
        }

        public override int PriorityFor(NameType type) {
            // last option
            return -10000;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.Being;
        }

        public override float ChanceFor(NameRequest request) {
            return 1f;
        }

        private void ConsumeName(NameResult res) {
            Consume(res.BeingName.Nickname);
            Consume(res.BeingName.FirstName, res.BeingName.LastName);
        }

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            res = new NameResult(ConsumeName);
            var sp = req.Species;
            int attempts = 50;
            var n = new BeingName();
            while (attempts-- > 0) {
                var nameGen = GenNamesLegacy.For(sp, req.Sex);
                if (nameGen != null) {
                    nameGen.Generate(req.Rng, n, sp, true);
                    if (IsConsumed(n.Nickname) || IsConsumed(n.FirstName, n.LastName)) {
                        n.Reset();
                        continue;
                    }
                    // backlink for consumption
                    n.NameResult = res;
                } else {
                    // don't care about uniqueness for crawlers
                    // Crawler
                    n.Nickname = sp.Name;
                }
                res.SetBeingName(n);

                return true;
            }
            return false;
        }

    }
}
