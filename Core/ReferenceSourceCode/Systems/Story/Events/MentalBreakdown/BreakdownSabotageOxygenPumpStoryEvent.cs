using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownSabotageOxygenPumpStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownSabotageOxygenPump";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownSabotageOxygenPumpStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalSabotagedOxygenPump;
        protected override string Name => T.BreakdownBreakdownsabotage;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownsabotageDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
