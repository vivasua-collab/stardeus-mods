using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipEngineData {
        [Key("Device")] public ShipDeviceData Device;
        [Key("EngineDefId")] public string EngineDefId;
        [Key("Phase")] public ShipEnginePhase Phase;
        [Key("PhaseTicksLeft")] public int PhaseTicksLeft;
        [Key("PhaseTicksTotal")] public int PhaseTicksTotal;

        public static ShipEngineData Serialize(ShipEngine engine) {
            return new ShipEngineData {
                Device = ShipDeviceData.Serialize(engine.Device),
                EngineDefId = engine.EngineDef.Id,
                Phase = engine.Phase,
                PhaseTicksLeft = engine.PhaseTicksLeft,
                PhaseTicksTotal = engine.PhaseTicksTotal
            };
        }

        public static ShipEngine TryDeserialize(GameState s, ShipEngineData data, NPCShip ship) {
            try {
                var engineDef = ShipEngineDef.Get(data.EngineDefId);
                if (engineDef == null) {
                    D.Err("Skipping engine due to missing engine def id: {0}: {1}",
                        data.EngineDefId,
                        D.DumpInstance(data, forLog: true));
                    return null;
                }
                var device = ShipDeviceData.TryDeserialize(s, data.Device, ship);
                if (device == null) {
                    D.Err("Skipping engine due to failure of deserializing the device: {0}",
                        D.DumpInstance(data, forLog: true));
                    return null;
                }
                if (data.Phase == ShipEnginePhase.Undefined) {
                    data.Phase = ShipEnginePhase.Inactive;
                }
                var engine = new ShipEngine {
                    EngineDef = engineDef,
                    Device = device,
                    Phase = data.Phase,
                    PhaseTicksLeft = data.PhaseTicksLeft,
                    PhaseTicksTotal = data.PhaseTicksTotal
                };

                return engine;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipEngineData for ship: {0}",
                    ship, D.DumpInstance(data, forLog: true));
                return null;
            }
        }
    }
}
