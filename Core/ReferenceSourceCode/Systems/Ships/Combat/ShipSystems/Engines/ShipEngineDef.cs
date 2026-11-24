using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Data;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using UnityEngine.VFX;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipEngineDef {
        public string Id;
        public int IdH;
        public string DeviceId;
        public ShipDeviceDef Device;
        public ShipEngineType EngineType;
        public bool IsHyperdrive => EngineType == ShipEngineType.HyperdriveShort
                || EngineType == ShipEngineType.HyperdriveLong;
        public string EngineTypeId;
        public Def Def => Device.Def;
        public float Rarity => Device.Rarity;
        public int ChargeTimeMinutes;
        public float Evade;

        public static Dictionary<int, ShipEngineDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static ShipEngineDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipEngineDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipEngineDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipEngineDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LinkDevices() {
            List<ShipEngineDef> broken = null;
            foreach (var sh in All.Values) {
                var deviceId = $"Engine_{sh.Id}";
                sh.Device = ShipDeviceDef.Get(deviceId);
                if (sh.Device == null) {
                    D.Err("Engine device data not defined for: {0}, DeviceId not found: {1}",
                        sh.Id, deviceId);
                    broken ??= new();
                    broken.Add(sh);
                }
            }
            if (broken != null) {
                foreach (var w in broken) {
                    All.Remove(w.IdH);
                }
                D.Err("Removed {0} broken engine defs: {1}", broken.Count,
                    D.DumpCollection(broken));
            }
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipEngineDef>("Config/Ships/Devices/Engines");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);

                if (!Enums.TryFromString(type.EngineTypeId, out type.EngineType)) {
                    D.Err("ShipEngineDef EngineTypeId undefined: {0}: {1}",
                        type.Id, type.EngineTypeId);
                    type.EngineType = ShipEngineType.Thruster;
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipEngineDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
            Ready.When(WhenH.CoreLoaded2, LinkDevices);
        }

        public bool CanAddFor(NPCShip ship, out float w) {
            // TODO check class, etc
            w = Rarity;
            return true;
        }
    }
}
