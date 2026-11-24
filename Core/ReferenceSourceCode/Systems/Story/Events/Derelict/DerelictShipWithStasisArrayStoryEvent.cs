using Game.Components;
using Game.Procgen;
using KL.Randomness;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class DerelictShipWithStasisArrayStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithStasisArray";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithStasisArrayStoryEvent());
        }
        protected override float chanceOfSmallShip => 0f;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var s = ctx.S;
            var sum = 0;
            var arrays = s.Components.AllByType<StasisArrayComp>();
            if (Arrays.IsEmpty(arrays)) {
                return true;
            }
            foreach (StasisArrayComp arr in arrays) {
                if (arr.Tile.IsConstructed && arr.BeingCount > 0) {
                    sum += arr.BeingCount;
                }
            }
            return sum < 15;
        }

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new StasisAreaGenStage(5, 10, 0, 2, 3, 3, minIntegrity: 0.2f,
                doSomeDamageToTheArea: true));
        }

        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new SplitStasisAndCPUGenStage());
        }
    }
}
