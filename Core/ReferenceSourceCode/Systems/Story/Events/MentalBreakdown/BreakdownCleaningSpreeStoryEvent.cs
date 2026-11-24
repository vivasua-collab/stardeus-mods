using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class BreakdownCleaningSpreeStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownCleaningSpree";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownCleaningSpreeStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Sys.Dirt.DirtCount > 100;
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalWentOnCleaningSpree;
        protected override string Name => T.BreakdownCleaningSpree;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownCleaningSpreeDesc_V1.F(Texts.LinkToBeing(being));
        }
    }
}
