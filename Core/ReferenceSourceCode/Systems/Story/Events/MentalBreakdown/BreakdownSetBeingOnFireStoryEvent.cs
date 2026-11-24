using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownSetBeingOnFireStoryEvent
            : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownSetBeingOnFire";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new BreakdownSetBeingOnFireStoryEvent());
        }

        protected override MentalBreakdownBand Band => MentalBreakdownBand.Heavy;
        protected override int AIVar => AIVarsH.MentalSetBeingOnFire;

        protected override string Name => T.BreakdownBreakdowninsanity;

        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdowninsanityDesc_V1.F(Texts.LinkToBeing(being));
        }

        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return hook.Target is Being actor
                && AIBreakdownActionSetBeingOnFire.HasViableVictim(ctx.S, actor);
        }
    }
}
