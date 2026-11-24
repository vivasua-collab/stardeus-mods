using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipFluxData {
        [Key("State")] public ShipFluxState State;
        [Key("Capacity")] public int Capacity;
        [Key("Soft")] public float Soft;
        [Key("Hard")] public float Hard;
        [Key("HardPercent")] public float HardPercent;
        [Key("TotalPercent")] public float TotalPercent;

        public static ShipFluxData Serialize(ShipFlux flux) {
            return new ShipFluxData {
                State = flux.State,
                Capacity = flux.Capacity,
                Soft = flux.Soft,
                Hard = flux.Hard,
                HardPercent = flux.HardPercent,
                TotalPercent = flux.TotalPercent
            };
        }

        public static ShipFlux TryDeserialize(GameState s, ShipFluxData data, NPCShip ship) {
            try {
                var flux = new ShipFlux();
                flux.LoadShip(ship);
                var state = data.State;
                // I was dumb enough to not have Uninitialized in the beginning
                if (data.State == ShipFluxState.Undefined) {
                    data.State = ShipFluxState.OK;
                }
                flux.LoadState(data.State);
                flux.Capacity = data.Capacity;
                flux.Soft = data.Soft;
                flux.Hard = data.Hard;
                flux.HardPercent = data.HardPercent;
                flux.TotalPercent = data.TotalPercent;
                return flux;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipFluxData for ship: {0}: {1}",
                    ship.Name, D.DumpInstance(data, forLog: true));
                return null;
            }
        }
    }
}
