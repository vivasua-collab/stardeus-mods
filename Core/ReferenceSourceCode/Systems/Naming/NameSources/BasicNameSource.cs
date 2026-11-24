using Game.Constants;
using Game.Procgen.Names;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class BasicNameSource : NameSource {
        public const string SourceId = "Basic";

        [SelfInit]
        private static void Register() {
            Register(SourceId, new BasicNameSource());
        }

        public override int PriorityFor(NameType type) {
            // last option
            return int.MinValue;
        }

        public override bool Supports(NameRequest req) {
            return true;
        }

        public override float ChanceFor(NameRequest request) {
            return 1f;
        }

        private void ConsumeName(NameResult res) {
            if (res.Name != null) {
                // D.Warn("Consuming name: {0}", name);
                UsedNames.Add(Hashes.S(res.Name));
            }
        }

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            res = new NameResult(ConsumeName);
            switch (req.Type) {
                case NameType.Ship: {
                    res.SetName(GenNamesLegacy.For(SpaceObjectTypeId.Player).Generate(req.Rng));
                    return true;
                }
                case NameType.Being: {
                    var name = GenNamesLegacy.For(SpaceObjectTypeId.Planet).Generate(req.Rng);
                    res.SetBeingName(new BeingName() {
                        Nickname = name }
                    );
                    return true;
                }
                default: {
                    res.SetName(GenNamesLegacy.For(SpaceObjectTypeId.Planet).Generate(req.Rng));
                    return true;
                }
            }
        }

    }
}
