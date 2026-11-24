using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Firex {
    [MessagePackObject]
    public struct Flammable {
        public void RestoreFromSaved(in Flammable saved) {
            Idx = saved.Idx;
            FireIdx = saved.FireIdx;
            TicksOnFire = saved.TicksOnFire;
            EntityId = saved.EntityId;
            FireCycle = saved.FireCycle;
        }
        [IgnoreMember] public bool IsValid => EntityId != 0;
        // WARNING if adding new stuff, that is saved - update RestoreFromSaved!
        [Key("Idx")] public int Idx;
        [Key("FireIdx")] public int FireIdx;
        [Key("EntityId")] public int EntityId;
        [Key("TicksOnFire")] public int TicksOnFire;
        [Key("FireCycle")] public int FireCycle;
        [IgnoreMember] public bool IsWall;
        // For the walls, we will consume neighbor tile oxygen instead of our own
        [IgnoreMember] public int PosIdx;
        [IgnoreMember] public float SelfCombustTempKelvin;
        [IgnoreMember] public bool IsTempResistent;
        [IgnoreMember] public bool NeedsSync;
        [IgnoreMember] public bool IsOnFire => TicksOnFire > 0;

        // Job Outputs:
        [IgnoreMember] public bool CurrentShouldTrySelfCombust;
        public override string ToString() {
            return $"Flammable[idx:{Idx},{D.DumpInstance(this, true, forLog: true)}";
        }
    }
}
