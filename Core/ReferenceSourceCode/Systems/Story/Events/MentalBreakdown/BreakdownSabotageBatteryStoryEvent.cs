using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class BreakdownSabotageBatteryStoryEvent : MentalBreakdownStoryEvent {
        public const string EventId = "BreakdownSabotageBattery";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new BreakdownSabotageBatteryStoryEvent());
        }
        // A drained battery recharges on its own, so this is an annoyance rather
        // than a catastrophe like the reactor or the oxygen pump.
        protected override MentalBreakdownBand Band => MentalBreakdownBand.Medium;
        protected override int AIVar => AIVarsH.MentalSabotagedBattery;
        protected override string Name => T.BreakdownBreakdownsabotage;
        protected override string DescriptionFor(BeingData being) {
            return T.BreakdownBreakdownsabotageDesc_V1.F(Texts.LinkToBeing(being));
        }

        // Reject the event when there is no battery on the map so the action
        // doesn't end immediately with nothing to do.
        public override bool CheckHook(StoryContext ctx, StoryEventDef def,
                StoryEventHook hook) {
            return ctx.S.Components.CountByType<BatteryComp>() > 0;
        }
    }
}
