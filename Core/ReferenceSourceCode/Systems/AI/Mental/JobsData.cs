using System.Collections.Generic;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Mental {
    [MessagePackObject]
    public struct JobsData {
        [Key("Slots")] public JobSlotData[] Slots;

        public static JobsData Serialize(Jobs jobs) {
            if (jobs == null) {
                return default;
            }
            var d = new JobsData();
            d.Slots = new JobSlotData[jobs.JobSlots.Count];
            int i = 0;
            foreach (var sl in jobs.JobSlots.Values) {
                d.Slots[i++] = JobSlotData.Serialize(sl);
            }
            return d;
        }

        public static Jobs Deserialize(Mind mind, JobsData data) {
            var jobs = new Jobs();
            jobs.Owner = mind;
            if (Arrays.IsEmpty(data.Slots)) { return jobs; }
            foreach (var sl in data.Slots) {
                if (JobSlotData.TryDeserialize(jobs.Owner, sl, out var slot)) {
                    jobs.JobSlots.Add(slot.Job.IdHash, slot);
                }
            }
            return jobs;
        }
    }
}
