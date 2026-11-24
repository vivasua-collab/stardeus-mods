using Game.Constants;
using Game.Data;
using Game.CodeGen;
using Game.Utils;

namespace Game.Components {
    public sealed class BedUserComp : BaseComponent<BedUserComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BedUserComp());
        }

        public bool AutoAssign;
        protected override void OnConfig() {
            AutoAssign = Config.GetBool(PropertyIdH.AutoAssign, true);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("AutoAssign", AutoAssign);
        }

        protected override void OnLoad(ComponentData data) {
            AutoAssign = data.GetBool("AutoAssign", AutoAssign);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                S.Query.AssignBedSlot.Ask(Being.Data, null);
            }
        }
    }
}
