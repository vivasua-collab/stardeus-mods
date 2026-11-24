using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Data;
using Game.Systems.Firex;
using Game.Utils;
using KL.Grid;
using KL.Randomness;

namespace Game.Systems.AI {
    // "Sabotage a toilet" breakdown. The being walks to a nearby toilet, lingers
    // near it, then walks away. When the breakdown ends the toilet explodes and
    // sprays human-waste dirt all around it (via ToiletExplosion, the same logic
    // used by CleaningIncidentStoryEvent).
    public sealed class AIBreakdownActionSabotageToilet : AIBreakdownActionSabotageDevice {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionSabotageToilet());
        }
        public const string ActId = "BreakdownSabotageToilet";

        protected override int AiVar => AIVarsH.MentalSabotagedToilet;

        private AIBreakdownActionSabotageToilet() : base(ActId) { }

        protected override bool IsSabotageTarget(Tile t) {
            if (t.TryGetComponent<ToiletComp>(out var toilet)) {
                if (t.HasComponent<CleanableComp>()) {
                    return true;
                }
                // this is a planter, but for shits and giggles, let's allow
                // sabotaging it too sometimes
                return Rng.UChance(0.1f);
            }
            return false;
        }

        protected override void PerformSabotage(AIAgentComp agent, Tile target) {
            ToiletExplosion.Explode(agent.S, target, Rng.Unseeded, Rng.URange(0.5f, 1f));
        }
    }
}
