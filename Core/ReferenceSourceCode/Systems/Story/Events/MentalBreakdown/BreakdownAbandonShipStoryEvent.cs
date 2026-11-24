using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownAbandonShipStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownAbandonShip";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownAbandonShipStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalAbandonedShip;
        protected override string Name => T.BreakdownBreakdownabandonship;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownabandonshipDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
