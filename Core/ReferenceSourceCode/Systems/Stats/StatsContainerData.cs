using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Stats {
    [MessagePackObject]
    public struct StatsContainerData {
        [Key("EntityId")] public int EntityId;
        [Key("Stats")] public StatData[] Stats;

        public static StatsContainerData Serialize(StatsContainer container) {
            if (container == null) { return default; }
            var data = new StatsContainerData();
            data.Stats = new StatData[container.AllStats.Length];
            for (int i = container.AllStats.Length - 1; i >= 0; i--) {
                Stat stat = container.AllStats[i];
                data.Stats[i] = StatData.Serialize(stat);
            }
            return data;
        }

        public static void Deserialize(StatsContainer stats,
                StatsContainerData container) {
            if (container.Stats == null) {
                // We have a blank stats for entity, let's create a new instance
                return;
            }
            foreach (var stat in stats.AllStats) {
                if (!container.TryLoadStat(stat)) {
                    // stat not found in save file
                    #if PRODUCTION_BUILD
                    D.Warn("Stat failed to load: {0}", stat, stats);
                    #else
                    D.Log("Stat failed to load: {0}", stat, stats);
                    #endif
                }
            }
        }

        private bool TryLoadStat(Stat stat) {
            if (stat.IsSourcesDirty) {
                stat.RecalculateBase();
            }
            for (int i = Stats.Length - 1; i >= 0; i--) {
                StatData s = Stats[i];
                if (s.Id == stat.Id) {
                    StatData.Deserialize(s, stat);
                    return true;
                }
            }
            return false;
        }
    }
}
