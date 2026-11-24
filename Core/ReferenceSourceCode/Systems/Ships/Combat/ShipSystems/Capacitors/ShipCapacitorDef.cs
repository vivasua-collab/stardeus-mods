using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipCapacitorDef {
        public string Id;
        public int IdH;
        public string DeviceId;
        public ShipDeviceDef Device;
        public Def Def => Device.Def;
        public float Rarity => Device.Rarity;
        public int Capacity;
        public int ChargeTimeMinutes;

        public static Dictionary<int, ShipCapacitorDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static ShipCapacitorDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipCapacitorDef not found by id: {0}", id);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        public static ShipCapacitorDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipCapacitorDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipCapacitorDef>("Config/Ships/Devices/Capacitors");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipCapacitorDef with such ID already exists: {0}", type.Id);
                }
            }
            Ready.When(WhenH.CoreLoaded2, LinkDevices);
        }

        private static void LinkDevices() {
            List<ShipCapacitorDef> broken = null;
            foreach (var cap in All.Values) {
                var deviceId = $"Capacitor_{cap.Id}";
                cap.Device = ShipDeviceDef.Get(deviceId);
                if (cap.Device == null) {
                    D.Err("Capacitor device data not defined for: {0}, DeviceId not found: {1}",
                        cap.Id, deviceId);
                    broken ??= new();
                    broken.Add(cap);
                }
            }
            if (broken != null) {
                foreach (var w in broken) {
                    All.Remove(w.IdH);
                }
                D.Err("Removed {0} broken capacitor defs: {1}", broken.Count,
                    D.DumpCollection(broken));
            }
        }

        public bool CanAddFor(NPCShip ship, out float w) {
            // TODO check class, etc
            w = Rarity;
            return true;
        }
    }
}
