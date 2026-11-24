using Game.CodeGen;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class StoreBeingBioAction : StoreBeingActionBase {
        public const string ActionId = "StoreBeingBio";

        [SelfInit]
        private static void Register() {
            RegisterAction(new StoreBeingBioAction());
        }

        public override string Id => ActionId;
        protected override bool AcceptsBiological => true;
    }
}
