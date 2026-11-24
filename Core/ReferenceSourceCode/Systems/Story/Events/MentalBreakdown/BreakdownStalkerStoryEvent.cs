using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownStalkerStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownStalker";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownStalkerStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalStalked;
        protected override string Name => T.BreakdownStalker;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownStalkerDesc_V1.F(Texts.LinkToBeing(being));
        }

        // A being that is alone has nobody to follow.
        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return hook.Target is Being actor
                && AIBreakdownActionStalker.HasViableVictim(ctx.S, actor);
        }
    }
}
