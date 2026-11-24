using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownPanicStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownPanic";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownPanicStoryEvent());
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Medium;
        protected override int AIVar => AIVarsH.MentalPanicked;
        protected override string Name => T.ActPanic;
        protected override string DescriptionFor(BeingData being) {
            return T.ActPanicDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
