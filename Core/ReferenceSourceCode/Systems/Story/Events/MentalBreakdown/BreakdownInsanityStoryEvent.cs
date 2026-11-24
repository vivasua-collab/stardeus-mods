using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownInsanityStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownInsanity";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownInsanityStoryEvent());
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalWentInsane;
        protected override string Name => T.BreakdownBreakdowninsanity;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdowninsanityDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
