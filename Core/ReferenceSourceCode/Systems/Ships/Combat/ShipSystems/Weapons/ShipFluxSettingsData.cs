using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public sealed class ShipFluxSettingsData {
        [Key("AutoVentFlux")] public bool AutoVentFlux;
        [Key("IsVentingManuallyNow")] public bool IsVentingManuallyNow;
        [Key("ToleratedFluxTotal")] public float ToleratedFluxTotal;
        [Key("ToleratedFluxHard")] public float ToleratedFluxHard;
        [Key("VentFluxStopHard")] public float VentFluxStopHard;
        [Key("VentFluxStopTotal")] public float VentFluxStopTotal;

        public static ShipFluxSettingsData Serialize(ShipFluxSettings settings) {
            if (settings == null) { return null; }
            return new ShipFluxSettingsData {
                AutoVentFlux = settings.AutoVentFlux,
                IsVentingManuallyNow = settings.IsVentingManuallyNow,
                ToleratedFluxTotal = settings.ToleratedFluxTotal,
                ToleratedFluxHard = settings.ToleratedFluxHard,
                VentFluxStopHard = settings.VentFluxStopHard,
                VentFluxStopTotal = settings.VentFluxStopTotal
            };
        }

        public static ShipFluxSettings Deserialize(ShipFluxSettingsData data) {
            if (data == null) { return null; }
            return new ShipFluxSettings {
                AutoVentFlux = data.AutoVentFlux,
                IsVentingManuallyNow = data.IsVentingManuallyNow,
                ToleratedFluxTotal = data.ToleratedFluxTotal,
                ToleratedFluxHard = data.ToleratedFluxHard,
                VentFluxStopHard = data.VentFluxStopHard,
                VentFluxStopTotal = data.VentFluxStopTotal
            };
        }
    }
}
