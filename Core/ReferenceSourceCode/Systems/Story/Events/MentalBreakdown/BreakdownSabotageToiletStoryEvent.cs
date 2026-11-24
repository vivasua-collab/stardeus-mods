using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownSabotageToiletStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownSabotageToilet";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownSabotageToiletStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalSabotagedToilet;
        protected override string Name => T.BreakdownBreakdownsabotage;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownsabotageDesc_V1.F(Texts.LinkToBeing(being));
        }

        // Reject the event when there is no toilet on the map so the action
        // doesn't end immediately with nothing to do.
        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return ctx.S.Components.CountByType<ToiletComp>() > 0;
        }
    }
}
