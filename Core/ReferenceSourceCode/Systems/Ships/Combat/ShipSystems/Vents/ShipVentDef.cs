using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Data;
using KL.Utils;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipVentDef {
        public string Id;
        public int IdH;
        public string DeviceId;
        public ShipDeviceDef Device;
        public Def Def => Device.Def;
        public float Rarity => Device.Rarity;
        public float VentBonus;

        public static Dictionary<int, ShipVentDef> All = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public static ShipVentDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipVentDef not found by id: {0}", id);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        public static ShipVentDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipVentDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipVentDef>("Config/Ships/Devices/Vents");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipVentDef with such ID already exists: {0}", type.Id);
                }
            }
            Ready.When(WhenH.CoreLoaded2, LinkDevices);
        }

        private static void LinkDevices() {
            List<ShipVentDef> broken = null;
            foreach (var vent in All.Values) {
                var deviceId = $"Vent_{vent.Id}";
                vent.Device = ShipDeviceDef.Get(deviceId);
                if (vent.Device == null) {
                    D.Err("Vent device data not defined for: {0}, DeviceId not found: {1}",
                        vent.Id, deviceId);
                    broken ??= new();
                    broken.Add(vent);
                }
            }
            if (broken != null) {
                foreach (var w in broken) {
                    All.Remove(w.IdH);
                }
                D.Err("Removed {0} broken vent defs: {1}", broken.Count,
                    D.DumpCollection(broken));
            }
        }

        public bool CanAddFor(NPCShip ship, out float w) {
            w = Rarity;
            return true;
        }
    }
}
