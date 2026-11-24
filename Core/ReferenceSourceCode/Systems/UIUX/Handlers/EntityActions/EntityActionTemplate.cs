using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityActionTemplate : BaseEntityAction {
        public const string ActionId = "TODO";
        [SelfInit]
        private static void Register() {
            // RegisterAction(new EntityActionTemplate());
        }
        public override string Id => ActionId;
    }

}
