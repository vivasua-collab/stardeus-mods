using System.Collections.Generic;
using Game.Components;
using Game.Input;
using Game.Mods;
using Game.Systems;
using KL.Collections;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class SystemsData {
        [Key("Data")] public Dictionary<string, ComponentData> Data;

        //[Key("RawData")] public Dictionary<string, object> RawData;

        [Key("SpecialData")] public SystemsDataSpecial SpecialData;

        public static SystemsData Serialize(GameSystems systems) {
            var sd = new SystemsData();
            sd.Data = new Dictionary<string, ComponentData>();
            //sd.RawData = new Dictionary<string, dynamic>();
            sd.SpecialData = new SystemsDataSpecial();
            foreach (var sys in systems.Active) {
                try {
                    if (sys is ISaveable sv) {
                        var data = sv.Save();
                        sd.Data[sys.Id] = data;
                    }
                    if (sys is ISaveableSpecial svr) {
                        svr.SaveSpecial(sd.SpecialData);
                    }
                } catch (System.Exception ex) {
                    Ready.WhenMainThread("SystemsData.Serialize", "Log failed saving", () => {
                        D.LogEx(ex, "Failed saving system data: {0}. Ready: {1}",
                            sys.Id, Ready.LongStatus);
                        if (ModRegistry.IsModType(sys.GetType(), out var mod)) {
                            ModRegistry.ReportBrokenModSave(sys.GetType(), mod);
                        } else {
                            var msg = $"Failed saving system data: {sys.Id}:{GlyphMap.BRHR}{ex}{GlyphMap.BRHR}";
                            ModRegistry.ReportBrokenSave(sys.GetType(), msg);
                        }
                    });
                }
            }
            return sd;
        }

        public static void Deserialize(GameSystems systems, SystemsData data) {
            if (data == null || data.Data == null) { return; }
            foreach (var sys in systems.Known.Values) {
                try {
                    if (sys is ISaveable sv) {
                        var d = data.Data.Get(sys.Id, null);
                        if (d != null) {
                            sv.Load(d);
                        }
                    }
                    if (sys is ISaveableSpecial svr) {
                        svr.LoadSpecial(data.SpecialData);
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed restoring game system data after load: {0}",
                        sys.Id);
                    if (ModRegistry.IsModType(sys.GetType(), out var mod)) {
                        ModRegistry.ReportBrokenModSave(sys.GetType(), mod);
                    } else {
                        var msg = $"Failed restoring system data: {sys.Id}:{GlyphMap.BRHR}{ex}{GlyphMap.BRHR}";
                        ModRegistry.ReportBrokenSave(sys.GetType(), msg);
                    }
                }
            }
        }
    }
}
