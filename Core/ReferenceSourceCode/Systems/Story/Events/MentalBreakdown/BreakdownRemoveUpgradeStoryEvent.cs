using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownRemoveUpgradeStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownRemoveUpgrade";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownRemoveUpgradeStoryEvent());
        }
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Light;
        protected override int AIVar => AIVarsH.MentalRemovedUpgrade;
        protected override string Name => T.BreakdownBreakdownremoveupgrade;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownremoveupgradeDesc_V1.F(Texts.LinkToBeing(being));
        }

        // Reject the event when nothing on the map has an upgrade installed so the
        // action doesn't end immediately with nothing to do.
        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return ctx.S.Sys.Upg.Upgraded.Count > 0;
        }
    }
}
