using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    // Target availability is checked by the action itself (CanBeAssignedTo),
    // so that selection and execution agree on what a valid target is.
    public sealed class BreakdownPickFightStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownPickFight";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownPickFightStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Medium;
        protected override int AIVar => AIVarsH.MentalPickedFight;
        protected override string Name => T.BreakdownPickFight;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownPickFightDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
