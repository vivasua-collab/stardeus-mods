using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownStartFireStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownStartFire";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownStartFireStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalStartedFires;
        protected override string Name => T.BreakdownBreakdownstartfire;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownstartfireDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
