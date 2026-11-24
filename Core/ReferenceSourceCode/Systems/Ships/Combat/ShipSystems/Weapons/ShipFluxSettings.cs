using System;
using Game.Data;

namespace Game.Systems.Ships {
    public sealed class ShipFluxSettings {
        public bool AutoVentFlux;
        /// <summary>
        /// For when the player starts venting
        /// </summary>
        public bool IsVentingManuallyNow;
        public float ToleratedFluxTotal;
        public float ToleratedFluxHard;
        public float VentFluxStopHard;
        public float VentFluxStopTotal;

        public static ShipFluxSettings Default {
            get {
                return new ShipFluxSettings() {
                    AutoVentFlux = true,
                    ToleratedFluxHard = 0.75f,
                    ToleratedFluxTotal = 0.9f,
                    VentFluxStopHard = 0.25f,
                    VentFluxStopTotal = 0.33f,
                };
            }
        }

        public bool WantsVent(ShipFlux flux) {
            if (flux.TotalPercent > ToleratedFluxTotal) { return true; }
            if (flux.HardPercent > ToleratedFluxHard) { return true; }
            return false;
        }

        public bool WantsVentStop(ShipFlux flux) {
            return flux.TotalPercent < VentFluxStopTotal
                && flux.HardPercent < VentFluxStopHard;
        }
    }
}
