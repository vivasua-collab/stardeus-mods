#if UNITY_EDITOR
// extremely noisy!
// #define DEBUG_COUNTERS
#endif
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class Counters {
        [Key("Data")] public Dictionary<int, int> Data;
        private object context;
        public void Initialize(object context) {
            this.context = context;
            Data ??= new();
        }
        public void Increment(int counterIdH) {
            if (!Data.TryGetValue(counterIdH, out var cnt)) {
                cnt = 0;
            }
            #if DEBUG_COUNTERS
            D.Warn("Incrementing Counter: {0}->{1} ({2})",
                CounterIdH.ResolveName(counterIdH), cnt, context);
            #endif
            Data[counterIdH] = cnt + 1;
        }
        public int Get(int counterIdH) {
            if (Data.TryGetValue(counterIdH, out var cnt)) {
                return cnt;
            }
            return 0;
        }
        public void Set(int counterIdH, int val) {
            #if DEBUG_COUNTERS
            D.Warn("Setting Counter: {0}->{1} ({2})",
                CounterIdH.ResolveName(counterIdH), val, context);
            #endif
            Data[counterIdH] = val;
        }

        public string DebugDump() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendFormat("Counters for {0}:\n", context);
            foreach (var cnt in Data) {
                sb.AppendFormat("- {0}: {1}\n",
                    CounterIdH.ResolveName(cnt.Key), cnt.Value);
            }
            return sb.ToString();
        }
    }
}
