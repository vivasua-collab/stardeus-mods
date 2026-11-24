using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    // Target availability is checked by the action itself (CanBeAssignedTo),
    // so that selection and execution agree on what a valid target is.
    public sealed class BreakdownLightsOutStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownLightsOut";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownLightsOutStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalSwitchedDevicesOff;
        protected override string Name => T.BreakdownLightsOut;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownLightsOutDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
