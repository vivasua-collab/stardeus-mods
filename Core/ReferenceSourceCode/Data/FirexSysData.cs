using System;
using Game.Systems.Firex;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class FirexSysData {
        [Key("Flams")] public Flammable[] Flams;
        [Key("FlamsCount")] public int FlamsCount;
        [Key("FlamsRecycler")] public int[] FlamsRecycler;
        [Key("Fires")] public Fire[] Fires;
        [Key("FiresCount")] public int FiresCount;
        [Key("FiresRecycler")] public int[] FiresRecycler;
        [Key("FireBoostUntil")] public long FireBoostUntilTicks;
        [Key("FireCycle")] public int FireCycle;
    }
}
