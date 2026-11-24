using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownInsultSpreeStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownInsultSpree";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownInsultSpreeStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Medium;
        protected override int AIVar => AIVarsH.MentalWentOnInsultSpree;
        protected override string Name => T.BreakdownInsultSpree;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownInsultSpreeDesc_V1.F(Texts.LinkToBeing(being));
        }

        // An insult needs someone who can take offense.
        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return hook.Target is Being actor
                && AIBreakdownActionInsultSpree.HasViableVictim(ctx.S, actor);
        }
    }
}
