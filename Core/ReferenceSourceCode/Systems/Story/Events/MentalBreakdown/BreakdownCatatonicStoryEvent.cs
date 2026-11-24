using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownCatatonicStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownCatatonic";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownCatatonicStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Medium;
        protected override int AIVar => AIVarsH.MentalWentCatatonic;
        protected override string Name => T.BreakdownCatatonic;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownCatatonicDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
