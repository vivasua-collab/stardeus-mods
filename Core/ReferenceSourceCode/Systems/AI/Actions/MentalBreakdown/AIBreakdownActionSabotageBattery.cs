using Game.AI;
using Game.CodeGen;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.AI {
    // "Sabotage a battery" breakdown. The being walks to a nearby battery that
    // still holds a charge, lingers near it, then walks away. When the breakdown
    // ends the battery is shorted and loses all of its stored energy.
    public sealed class AIBreakdownActionSabotageBattery : AIBreakdownActionSabotageDevice {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionSabotageBattery());
        }
        public const string ActId = "BreakdownSabotageBattery";

        // Draining an almost empty battery would be a sabotage nobody notices.
        public const float MinChargePercent = 0.1f;

        protected override int AiVar => AIVarsH.MentalSabotagedBattery;

        private AIBreakdownActionSabotageBattery() : base(ActId) { }

        protected override bool IsSabotageTarget(Tile t) {
            return t.TryGetComponent<BatteryComp>(out var battery)
                && battery.ENode != null
                && battery.BatteryMaxCharge > 0f
                && battery.ChargePercent > MinChargePercent;
        }

        protected override void PerformSabotage(AIAgentComp agent, Tile target) {
            if (target.TryGetComponent<BatteryComp>(out var battery)) {
                battery.ENode?.SetBatteryCharge(0f);
            }
        }
    }
}
