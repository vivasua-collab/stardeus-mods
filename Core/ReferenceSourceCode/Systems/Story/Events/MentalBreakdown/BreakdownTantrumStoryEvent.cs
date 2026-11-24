using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    // Target availability is checked by the action itself (CanBeAssignedTo),
    // so that selection and execution agree on what a valid target is.
    public sealed class BreakdownTantrumStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownTantrum";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownTantrumStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalThrewTantrum;
        protected override string Name => T.BreakdownTantrum;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownTantrumDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
