using System;
using System.Collections.Generic;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public sealed class FunSourceUsesData {
        [Key("A")] public byte Activity;
        [Key("S")] public int SubjectIdH;
        [Key("T")] public long[] Ticks;
    }

    [MessagePackObject]
    public sealed class FunMemoryData {
        [Key("Sources")] public List<FunSourceUsesData> Sources;

        // Expired uses are dropped here, so minds nobody ticks anymore (dead, orphaned)
        // don't keep old history in the save forever.
        public static FunMemoryData Serialize(FunMemory memory, long ticks, long windowTicks) {
            if (memory == null || memory.SourceCount == 0) {
                return null;
            }
            List<FunSourceUsesData> sources = null;
            foreach (var kv in memory) {
                var list = kv.Value;
                int first = 0;
                while (first < list.Count && ticks - list[first] >= windowTicks) {
                    first++;
                }
                int count = list.Count - first;
                if (count <= 0) { continue; }
                var arr = new long[count];
                list.CopyTo(first, arr, 0, count);
                sources ??= new();
                sources.Add(new FunSourceUsesData() {
                    Activity = (byte) kv.Key.Activity,
                    SubjectIdH = kv.Key.SubjectIdH,
                    Ticks = arr,
                });
            }
            if (sources == null) {
                return null;
            }
            return new FunMemoryData() { Sources = sources };
        }

        // Sources that isKnown rejects (e.g. a device from a removed mod) are dropped instead of
        // failing the load or keeping memories nothing can resolve anymore
        public static void Deserialize(FunMemoryData data, FunMemory memory,
                Predicate<FunSource> isKnown, uint mindId) {
            if (data?.Sources == null) { return; }
            foreach (var s in data.Sources) {
                if (s == null) { continue; }
                var src = new FunSource((FunActivity) s.Activity, s.SubjectIdH);
                if (!isKnown(src)) {
                    D.Err("Dropping fun memory of unknown subject {0} for mind {1}, was a mod removed?",
                        src, mindId);
                    continue;
                }
                memory.Restore(src, s.Ticks);
            }
        }
    }
}
