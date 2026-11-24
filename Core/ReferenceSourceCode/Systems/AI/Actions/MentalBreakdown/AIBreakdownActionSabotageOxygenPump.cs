using Game.AI;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.AI {
    // "Sabotage an oxygen pump" breakdown. The being walks to a nearby oxygen
    // pump, lingers near it, then walks away. When the breakdown ends the pump
    // catches on fire.
    public sealed class AIBreakdownActionSabotageOxygenPump : AIBreakdownActionSabotageDevice {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionSabotageOxygenPump());
        }
        public const string ActId = "BreakdownSabotageOxygenPump";

        protected override int AiVar => AIVarsH.MentalSabotagedOxygenPump;

        private AIBreakdownActionSabotageOxygenPump() : base(ActId) { }

        protected override bool IsSabotageTarget(Tile t) {
            return t.TryGetComponent<OxygenProducerComp>(out _);
        }

        protected override void PerformSabotage(AIAgentComp agent, Tile target) {
            agent.S.CmdQ.Enqueue(new CmdStartFire(target.CenterIdx));
        }
    }
}
