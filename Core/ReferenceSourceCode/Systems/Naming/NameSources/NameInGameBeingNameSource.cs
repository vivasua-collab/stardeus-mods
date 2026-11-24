using Game.Constants;
using Game.Procgen.Names;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Naming {
    public sealed class NameInGameBeingNameSource : NameSource {
        public const string SourceId = "NameInGameBeing";

        public const BeingType supportedBeingTypes =
            BeingType.StartingCrew |
            BeingType.StasisWakeup |
            BeingType.Friendly |
            BeingType.Clone |
            BeingType.Newborn |
            BeingType.Enemy;

        [SelfInit]
        private static void Register() {
            Register(SourceId, new NameInGameBeingNameSource());
        }

        public override int PriorityFor(NameType type) {
            // not last but below default 0
            return -1000;
        }

        public override bool Supports(NameRequest req) {
            return req.Type == NameType.Being && (req.BeingType & supportedBeingTypes) != BeingType.None;
        }

        public override float ChanceFor(NameRequest request) {
            return Tunables.CustomPersonaTemplateChance;
        }

        private void ConsumeName(NameResult res) {
            if (res.InternalResult is NameInGame tpl) {
                UsedNames.Add(tpl.IdH);
            }
        }

        public override bool TryNameFor(NameRequest req, out NameResult res) {
            var tpl = NameInGame.RandomFor(req, UsedNames);
            if (tpl == null) {
                res = null;
                // couldn't get a name
                return false;
            }

            res = new NameResult(ConsumeName);
            var sp = req.Species;
            var n = new BeingName();
            n.FirstName = tpl.FirstName;
            n.LastName = tpl.LastName;
            n.Nickname = tpl.Nickname;
            res.SetBeingName(n, tpl);
            return true;
        }
    }
}
