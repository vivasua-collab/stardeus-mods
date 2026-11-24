using System.Collections.Generic;
using MessagePack;

namespace Game.Systems.Statistics {
    [MessagePackObject]
    public class StatisticsSysData {
        // key = Def.Id (string)
        [Key("Stock")] public Dictionary<string, SortedDictionary<long, int>> Stock;
        [Key("Prod")]  public Dictionary<string, SortedDictionary<long, int>> Produced;
        [Key("Cons")]  public Dictionary<string, SortedDictionary<long, int>> Consumed;
        // EntityKind byte: 0=Mat, 1=Obj, 2=Being
        [Key("Kind")]  public Dictionary<string, byte> Kind;
        // stock compression state: last written value and tick per Def.Id
        [Key("LastStock")]     public Dictionary<string, int>  LastStockValue;
        [Key("LastStockTick")] public Dictionary<string, long> LastStockTick;
    }
}
