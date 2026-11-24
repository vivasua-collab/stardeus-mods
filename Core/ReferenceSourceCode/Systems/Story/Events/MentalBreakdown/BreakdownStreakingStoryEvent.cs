using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownStreakingStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownStreaking";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownStreakingStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalStreaked;
        protected override string Name => T.BreakdownStreaking;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownStreakingDesc_V1.F(Texts.LinkToBeing(being));
        }

        // Nothing to throw off - nothing to watch.
        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return hook.Target is Being actor
                && AIBreakdownActionStreaking.HasEquipment(actor.Data);
        }
    }
}
