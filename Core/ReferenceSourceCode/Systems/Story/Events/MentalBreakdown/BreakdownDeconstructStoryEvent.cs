using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownDeconstructStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownDeconstruct";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new BreakdownDeconstructStoryEvent());
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalDeconstructed;
        protected override string Name => T.BreakdownDeconstruct;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdowndeconstructDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
