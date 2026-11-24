using Game.CodeGen;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class StoreBeingRoboAction : StoreBeingActionBase {
        public const string ActionId = "StoreBeingRobo";

        [SelfInit]
        private static void Register() {
            RegisterAction(new StoreBeingRoboAction());
        }

        public override string Id => ActionId;
        protected override bool AcceptsBiological => false;
    }
}
