using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Components {
    public sealed class MindjarComp : BaseComponent<MindjarComp>, ILooksFilter {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MindjarComp());
        }

        public bool FilterLooks(SpeciesLooksDef looksDef, BeingLooks looks) {
            var body = looks.Get(LookPartTypeDef.Body);
            if (Being.Data.Body.MindSlot.IsPartRemoved) {
                if (!body.SpriteId.Contains("Mindjar_Empty")) {
                    body.SpriteId = body.SpriteId.Replace("Mindjar", "Mindjar_Empty");
                    return true;
                }
            } else {
                if (body.SpriteId.Contains("Mindjar_Empty")) {
                    body.SpriteId = body.SpriteId.Replace("Mindjar_Empty", "Mindjar");
                    return true;
                }
            }
            return false;
        }

        public override string ToString() {
            return " * Mindjar";
        }
    }
}
