using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownRoamStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownRoam";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownRoamStoryEvent());
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalRoamed;
        protected override string Name => T.BreakdownBreakdownroamaround;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownroamaroundDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
