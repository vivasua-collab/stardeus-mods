using System.Collections.Generic;
using Game.Data;
using Game.Systems.Quests;
using Game.Utils;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Story {
    [MessagePackObject]
    public struct QuestSeenData {
        [Key("DefId")] public string DefId;
        [Key("StartCount")] public ushort StartCount;
        [Key("CompleteCount")] public ushort CompleteCount;
        [Key("FailCount")] public ushort FailCount;
        [Key("LastStart")] public long LastStart;
        [Key("LastSeen")] public long LastSeen;
        /// <summary>
        /// Decayed seen-count per text-variant index. Fades over time so "lately" matters.
        /// </summary>
        [Key("VariantSeen")] public float[] VariantSeen;
        /// <summary>
        /// Tick at which each variant was last marked as seen (parallel to VariantSeen).
        /// </summary>
        [Key("VariantSeenTick")] public long[] VariantSeenTick;

        public static void Deserialize(QuestSeenData[] saved,
                Dictionary<QuestDef, QuestSeenData> seen) {
            if (saved == null || saved.Length == 0) {
                return;
            }
            for (int i = saved.Length - 1; i >= 0; i--) {
                QuestSeenData seenData = saved[i];
                var questDef = QuestDef.Get(seenData.DefId);
                if (questDef == null) {
                    D.Warn("Could not find QuestDef by id {0} when deserializing QuestSeenData",
                        seenData.DefId);
                    continue;
                }
                seen[questDef] = seenData;
            }
        }

        public static QuestSeenData[] Serialize(Dictionary<QuestDef, QuestSeenData> seen) {
            if (seen == null || seen.Count == 0) {
                return null;
            }
            var result = new QuestSeenData[seen.Count];
            int i = 0;
            foreach (var s in seen.Values) {
                result[i++] = s;
            }
            return result;
        }

        public override readonly string ToString() {
            return $"QuestSeenData[{DefId}|Start:{StartCount}|Cpl:{CompleteCount}|Fail:{FailCount}|LastStart:{Units.TicksAgoFromNow(LastStart)}]";
        }
    }
}
