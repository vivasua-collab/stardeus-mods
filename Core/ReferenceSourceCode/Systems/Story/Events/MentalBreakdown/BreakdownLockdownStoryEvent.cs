using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    // Target availability is checked by the action itself (CanBeAssignedTo),
    // so that selection and execution agree on what a valid target is.
    public sealed class BreakdownLockdownStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownLockdown";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownLockdownStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Medium;
        protected override int AIVar => AIVarsH.MentalLockedDoors;
        protected override string Name => T.BreakdownLockdown;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownLockdownDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
