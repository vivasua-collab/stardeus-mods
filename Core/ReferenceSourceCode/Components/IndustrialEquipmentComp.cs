using Game.Data;
using Game.CodeGen;
using System.Text;
using Game.Constants;

using Game.Utils;
namespace Game.Components {
    public sealed class IndustrialEquipmentComp : BaseComponent<IndustrialEquipmentComp>,
            IDefDescriptionProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new IndustrialEquipmentComp());
        }

        public bool IsNoisy;
        public bool IsNoisyNow => IsNoisy && (Tile.ENode.IsConsuming || Tile.ENode.IsProducing);

        protected override void OnConfig() {
            IsNoisy = Config.GetBool(PropertyIdH.IsNoisy, true);
        }
        public override string ToString() {
            return " * IndustrialEquipment";
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            var config = def.ComponentConfigFor(NameHash, true);
            if (config == null) { return false; }
            Def.PrependAdditionalInfo(sb, addedAnything);
            // No "(Noisy)": most industrial devices are noisy, so it would only repeat itself
            sb.Append(T.NounIndustrialEquipment);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }
    }
}
