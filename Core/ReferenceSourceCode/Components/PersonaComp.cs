using Game.Data;
using Game.CodeGen;
using System.Text;
using Game.Systems.Creatures;
using Game.Utils;
using System.Linq;

namespace Game.Components {
    public sealed class PersonaComp : BaseComponent<PersonaComp>, IDefDescriptionProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PersonaComp());
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                // Health bar is displayed via Being.Persona.ShowInfoBar (stupid, I know).
                HideInfoBar();
            }
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            var species = Species.ForDef(def.IdH);
            var flagNames = species.Flags.Select(f => $"flag.{f}".T()).OrderBy(f => f);
            sb.AppendJoin(", ", flagNames);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }
        public override string ToString() {
            return " * PersonaComp";
        }
    }
}
