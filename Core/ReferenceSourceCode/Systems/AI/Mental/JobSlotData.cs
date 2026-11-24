using KL.Utils;
using MessagePack;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public struct JobSlotData {
        [Key("JobId")] public string JobId;
        [Key("Level")] public byte Level;
        [Key("Priority")] public byte Priority;
        [Key("LevelProgress")] public float LevelProgress;
        [Key("Flags")] public JobAffinityFlags Flags;
        [Key("XpToday")] public float XpToday;

        public static JobSlotData Serialize(JobSlot slot) {
            if (slot == null) { return default; }
            var d = new JobSlotData();
            d.JobId = slot.Job.Id;
            d.Level = slot.Level;
            d.Priority = slot.Priority;
            d.LevelProgress = slot.LevelProgress;
            d.Flags = slot.Flags;
            d.XpToday = slot.XpToday;
            return d;
        }

        public static bool TryDeserialize(Mind owner, JobSlotData data, out JobSlot slot) {
            if (data.JobId == null) {
                slot = null;
                return false;
            }
            if (!JobType.TryGet(data.JobId, out var job)) {
                D.Err("Failed loading job by id: {0}. Memory of this job type will be removed for: {1}",
                    data.JobId, owner);
                slot = null;
                return false;

            }
            slot = new JobSlot(job);
            // older saves allowed levels up to 255
            slot.Level = (byte) System.Math.Min((int) data.Level, JobSlot.MaxLevel);
            slot.LevelProgress = data.Level > JobSlot.MaxLevel ? 0f : data.LevelProgress;
            slot.XpToday = data.XpToday;
            slot.Priority = data.Priority > JobSlot.MaxPriority ? JobSlot.MaxPriority : data.Priority;
            slot.Flags = data.Flags;
            slot.Owner = owner;
            return true;
        }
    }
}
