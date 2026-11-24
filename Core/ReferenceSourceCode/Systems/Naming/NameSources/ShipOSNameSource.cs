using System.Collections.Generic;
using KL.Utils;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class ShipOSNameSource : NameSource {
        public const string SourceId = "ShipOS";

        [SelfInit]
        private static void Register() {
            Register(SourceId, new ShipOSNameSource());
        }

        public override int PriorityFor(NameType type) {
            return 0;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.ShipOS;
        }

        public override float ChanceFor(NameRequest request) {
            return 1f;
        }

        // Names are dealt from a shuffled queue so re-rolling in game setup
        // doesn't repeat a name until all of them were offered.
        // Intentionally not cleared on reset.
        private List<string> allNames;
        private readonly Queue<string> queue = new();
        private string lastName;

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            res = null;
            if (queue.Count == 0) {
                allNames ??= new List<string>(
                    The.ModLoader.LoadTextLines("Config/Generation/Names/ShipOS/Name"));
                if (allNames.Count == 0) { return false; }
                var list = new List<string>(allNames);
                Arrays.Shuffle(req.Rng, list);
                // Avoid repeating the last name right after the refill
                if (list.Count > 1 && list[0] == lastName) {
                    int swapIdx = req.Rng.Range(1, list.Count);
                    list[0] = list[swapIdx];
                    list[swapIdx] = lastName;
                }
                foreach (var name in list) {
                    queue.Enqueue(name);
                }
            }
            lastName = queue.Dequeue();
            res = new NameResult(r => Consume(r.Name));
            res.SetName(lastName);
            return true;
        }
    }
}
