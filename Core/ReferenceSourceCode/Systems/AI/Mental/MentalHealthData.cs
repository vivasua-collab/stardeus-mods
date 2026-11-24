using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Creatures;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public class MentalHealthData {
        [Key("CurrentEffects")] public List<MentalEffect> CurrentEffects;
        [Key("PastEffects")] public List<MentalEffect> PastEffects;
        [Key("LastBreakdownTime")] public long LastBreakdownTime;
        [Key("IgnoreDeadTill")] public long IgnoreDeadBodiesTill;
        [Key("TrendStr")] public string TrendStr;
        [Key("FunMemory")] public FunMemoryData FunMemory;

        public static MentalHealthData Serialize(MentalHealth health) {
            if (health == null) {
                return default;
            }
            var d = new MentalHealthData();
            d.CurrentEffects = health.CurrentEffects;
            d.PastEffects = health.PastEffects;
            d.LastBreakdownTime = health.LastBreakdownTime;
            d.IgnoreDeadBodiesTill = health.IgnoreDeadBodiesUntil;
            d.TrendStr = health.Trend.Value;
            d.FunMemory = FunMemoryData.Serialize(health.FunMemory, G.Ticks,
                FunNovelty.WindowTicks);
            return d;
        }

        private static readonly Predicate<FunSource> isFunSubjectKnown = FunSubjects.IsKnown;

        public static MentalHealth Deserialize(Mind mind, MentalHealthData data) {
            if (data == null) {
                return MentalHealth.TryNewFor(mind);
            }
            var health = new MentalHealth();
            health.CurrentEffects.Clear();
            health.CurrentEffects.AddRange(data.CurrentEffects);
            health.Trend.SetSilent(data.TrendStr);
            health.LastBreakdownTime = data.LastBreakdownTime;
            health.IgnoreDeadBodiesUntil = data.IgnoreDeadBodiesTill;
            health.PastEffects.Clear();
            health.PastEffects.AddRange(data.PastEffects);
            FunMemoryData.Deserialize(data.FunMemory, health.FunMemory,
                isFunSubjectKnown, mind.Id);
            health.Owner = mind;
            health.AfterLoad();
            return health;
        }
    }
}
