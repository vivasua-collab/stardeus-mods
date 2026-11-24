using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.AI {
    // "Sabotage an emergency siren" breakdown. The being walks to a nearby siren
    // that is currently off, lingers near it, then walks away. When the breakdown
    // ends the siren switches itself on — triggering a false alarm.
    public sealed class AIBreakdownActionSabotageSiren : AIBreakdownActionSabotageDevice {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionSabotageSiren());
        }
        public const string ActId = "BreakdownSabotageSiren";

        protected override int AiVar => AIVarsH.MentalSabotagedSiren;

        private AIBreakdownActionSabotageSiren() : base(ActId) { }

        // Only target sirens that are currently off — an already-active siren
        // would immediately satisfy the alarm goal without any drama.
        protected override bool IsSabotageTarget(Tile t) {
            return t.TryGetComponent<EmergencySirenComp>(out var s) && !s.IsAlertActive;
        }

        // RequestActivate sets isAlertWanted = true and activates once the siren
        // has power. The security system never forces manually-activated sirens off,
        // so the alarm persists until the player manually silences it.
        protected override void PerformSabotage(AIAgentComp agent, Tile target) {
            if (target.TryGetComponent<EmergencySirenComp>(out var s)) {
                s.RequestActivate();
            }
        }
    }
}
