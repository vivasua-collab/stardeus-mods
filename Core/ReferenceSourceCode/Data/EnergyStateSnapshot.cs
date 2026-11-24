using System;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public struct EnergyStateSnapshot {
        [Key("Tick")] public long Tick;
        // sum of: foreach battery: (battery max cap * charge)
        [Key("BatteryCharge")] public float BatteryCharge;
        [Key("BatteryOutput")] public float BatteryOutput;
        // With batteries
        [Key("Consumption")] public float Consumption;
        [Key("Deficit")] public float Deficit;
        // Without batteries
        [Key("MaxOutput")] public float MaxOutput;
        // Solar panels and dumb producers
        [Key("RenewOutput")] public float RenewableOutput;
        [Key("GridCount")] public int GridCount;
        [Key("GridSize")] public int GridSize;

        public static EnergyStateSnapshot Create(long tick, Situation s) {
            return new() {
                Tick = tick,
                Consumption = s.GridConsumption,
                Deficit = s.GridDeficit,
                MaxOutput = s.GridMaxOutput,
                GridCount = s.Grids,
                GridSize = s.GridSize,
                BatteryCharge = s.GridBatCharge,
                BatteryOutput = s.GridBatOutput,
                RenewableOutput = s.GridRenewableOutput,
            };
        }
    }
}
