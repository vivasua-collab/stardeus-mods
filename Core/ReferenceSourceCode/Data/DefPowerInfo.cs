using System;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Mods;
using Game.Utils;
using KL.Utils;

namespace Game.Data {
    // Power lines of the codex Properties block. Numbers only: the ranges speak for
    // themselves and explanations would bloat every device description
    public static class DefPowerInfo {
        public static DefPower Collect(Def def) {
            var power = new DefPower();
            if (def.Components == null) { return power; }
            // Some components (doors) exist on unpowered variants too
            if (def.ComponentConfigFor(ComponentIdH.ElectricNode, false) == null) {
                return power;
            }
            foreach (var comp in def.Components) {
                if (!The.Defs.TryGetPrototype<IDefPowerProvider>(comp.ComponentH, out var prov)) {
                    continue;
                }
                try {
                    prov.AddDefPower(def, comp, ref power);
                } catch (Exception ex) {
                    D.LogEx(ex, "Failure while loading AddDefPower for component {0}", prov);
                    ModRegistry.WarnAboutBrokenMod(prov, "Failed to generate item description");
                }
            }
            return power;
        }

        // Items of the Properties comma list, like the components' own one-line info.
        // Returns whether anything is in the list now
        public static bool Append(StringBuilder sb, Def def, bool addedAnything) {
            var power = Collect(def);
            if (power.IsEmpty) { return addedAnything; }
            if (power.HasConsumption) {
                AppendItem(sb, ref addedAnything, T.EnergyConsumption,
                    Range(power.ConsumeMin, power.ConsumeMax));
            }
            if (power.HasProduction) {
                AppendItem(sb, ref addedAnything, T.EnergyProduction,
                    Range(power.ProduceMin, power.ProduceMax));
            }
            if (power.BatteryCapacity > 0f) {
                AppendItem(sb, ref addedAnything, T.Battery,
                    Units.BatteryCapacity(power.BatteryCapacity));
                AppendItem(sb, ref addedAnything, T.BatteryDischargeRate,
                    Units.Electricity(power.BatteryOutput));
            }
            return addedAnything;
        }

        private static void AppendItem(StringBuilder sb, ref bool addedAnything, string label,
                string value) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            addedAnything = true;
            sb.Append(Texts.NoBr($"{label}: <b>{value}</b>"));
        }

        public static string Range(float min, float max) {
            if (float.IsPositiveInfinity(max)) {
                return $"≥{Units.Electricity(min)}";
            }
            if (Math.Abs(max - min) < 0.05f) {
                return Units.Electricity(max);
            }
            if (max < 999f) {
                return $"{min:0.#}-{Units.Electricity(max)}";
            }
            return $"{Units.Electricity(min)}-{Units.Electricity(max)}";
        }
    }
}
