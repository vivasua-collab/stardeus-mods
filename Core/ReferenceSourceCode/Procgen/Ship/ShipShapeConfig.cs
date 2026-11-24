using System;
using System.IO;
using KL.Utils;

namespace Game.Procgen {
    public struct ShipShapeConfig {
        public static ShipShapeConfig Default {
            get {
                return new ShipShapeConfig {
                    AlwaysFilledThresholdFrom = 0.1f,
                    AlwaysFilledThresholdTo = 0.3f,
                    XSolidFromFrom = 0.1f,
                    XSolidFromTo = 0.3f,
                    XSolidToFrom = 0.6f,
                    XSolidToTo = 0.9f,
                    CockpitWidthFrom = 0.1f,
                    CockpitWidthTo = 0.3f,
                    XCockpitStartFrom = 0.1f,
                    XCockpitStartTo = 0.9f,
                    CockpitHeightFrom = 0.2f,
                    CockpitHeightTo = 0.4f,
                    CockpitSolidChance = 0.5f,
                    NoiseScaleFrom = 5f,
                    NoiseScaleTo = 15f,
                    WidthFrom = 100,
                    WidthTo = 160,
                    HeightFrom = 80,
                    HeightTo = 100,
                    CellWidthFrom = 4,
                    CellWidthTo = 6,
                    CellHeightFrom = 3,
                    CellHeightTo = 5,
                };
            }
        }
        public static ShipShapeConfig ForSpaceStation() {
            return ForScenario("space_station");
        }

        public static ShipShapeConfig ForScenario(string id) {
            var path = The.ModLoader.TryFile($"Scenarios/{id}/ShipShapeConfig.json");
            if (path != null) {
                if (File.Exists(path)) {
                    var cfg = Json.Deserialize<ShipShapeConfig>(
                        The.ModLoader.LoadPatchedJson(path), $"ShipShape({path})");
                    return cfg;
                } else {
                    D.Warn("Could not find {0} for scenario {1}", path, id);
                }
            } else {
                D.Warn("Could not find Scenarios/{0}/ShipShapeConfig.json", id);
            }
            return Default;
        }
        public float WidthFrom;
        public float WidthTo;
        public float HeightFrom;
        public float HeightTo;
        public float CellWidthFrom;
        public float CellWidthTo;
        public float CellHeightFrom;
        public float CellHeightTo;

        public float AlwaysFilledThresholdFrom;
        public float AlwaysFilledThresholdTo;
        public float XSolidFromFrom;
        public float XSolidFromTo;
        public float XSolidToFrom;
        public float XSolidToTo;
        public float XCockpitStartFrom;
        public float XCockpitStartTo;
        public float CockpitWidthFrom;
        public float CockpitWidthTo;
        public float CockpitHeightFrom;
        public float CockpitHeightTo;
        public float NoiseScaleFrom;
        public float NoiseScaleTo;
        public float CockpitSolidChance;

    }
}
