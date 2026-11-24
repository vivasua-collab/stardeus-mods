using System;
using System.Collections.Generic;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Stats {
    [MessagePackObject]
    public struct StatData {
        [Key("Val")] public float Value;
        [Key("Base")] public float BaseValue;
        [Key("Id")] public int Id;

        public static StatData Serialize(Stat stat) {
            if (stat == null) { return default; }
            var data = new StatData();
            data.Id = stat.Id;
            data.Value = stat.Value;
            data.BaseValue = stat.BaseValue;
            return data;
        }

        public static void Deserialize(StatData s, Stat stat) {
            // ID is supposed to match
            if (s.Id != stat.Id) {
                D.Err("Trying to deserialize a wrong stat, id does not match! {0}->{1}",
                    D.DumpInstance(s), D.DumpInstance(stat));
                return;
            }
            stat.BaseValue = s.BaseValue;
            stat.LoadValue(s.Value);
            if (stat.Modifiers != null) {
                D.Err("Stat modifiers present during load! {0}",
                    D.DumpInstance(stat));
            }
            stat.IsDirty = true;
        }
    }
}
