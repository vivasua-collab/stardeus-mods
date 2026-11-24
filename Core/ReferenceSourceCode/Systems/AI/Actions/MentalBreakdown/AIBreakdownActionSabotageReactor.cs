using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.AI {
    // "Sabotage a reactor" breakdown. The being walks to a nearby matter reactor,
    // ejects any installed Fuse upgrades (so the safety can't absorb the blast),
    // lingers, then walks away. The reactor melts down when the breakdown ends.
    public sealed class AIBreakdownActionSabotageReactor : AIBreakdownActionSabotageDevice {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionSabotageReactor());
        }
        public const string ActId = "BreakdownSabotageReactor";

        protected override int AiVar => AIVarsH.MentalSabotagedReactor;

        private AIBreakdownActionSabotageReactor() : base(ActId) { }

        protected override bool IsSabotageTarget(Tile t) {
            return t.TryGetComponent<MatterReactorComp>(out _);
        }

        // Eject fuses before lingering so the safety can't blow in place of the reactor.
        protected override void OnArm(AIAgentComp agent, Tile target) {
            EjectFuses(target);
        }

        // Skip the meltdown if the reactor was already destroyed (e.g. by a separate event).
        protected override bool CanSabotage(Tile target) {
            return target.Health != null && !target.Health.IsDestroyed;
        }

        // Trigger the meltdown by routing lethal Energy damage through the reactor's
        // own HealthWentToZero handler.
        protected override void PerformSabotage(AIAgentComp agent, Tile target) {
            target.Health.TakeDamage(target.Health.Health, DamageType.Energy);
        }

        // Pop out any installed fuse upgrades as physical items so the safety fuses
        // can't blow in place of the reactor when it melts down.
        private static void EjectFuses(Tile reactor) {
            var upgrades = reactor.Upgrades;
            if (upgrades?.Upgrades == null) { return; }
            var fuse = UpgradeRegistry.Get(FuseUpgrade.Id);
            for (int i = upgrades.Upgrades.Length - 1; i >= 0; i--) {
                if (upgrades.Upgrades[i] == FuseUpgrade.Id) {
                    upgrades.EjectSlot(i, fuse);
                }
            }
        }
    }
}
