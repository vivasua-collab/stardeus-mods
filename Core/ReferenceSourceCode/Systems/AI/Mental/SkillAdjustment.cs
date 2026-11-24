using System;
using System.Text;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.Mental {
    [Serializable]
    public struct SkillAdjustment {
        public string JobId;
        public JobType Job;
        public sbyte Adjustment;
        public static SkillAdjustment Deserialize(SkillAdjustment adj, object ctx) {
            if (JobType.TryGet(adj.JobId, out var job)) {
                if (job.Unskilled) {
                    D.Err("Skill adjustment is created for an unskilled job type! {0}: {1}",
                        adj.JobId, ctx);
                } else {
                    adj.Job = job;
                }
            } else {
                D.Err("Skill adjustment has invalid job id {0} on: {1}",
                    adj.JobId, ctx);
            }
            if (adj.Adjustment == 0) {
                D.Err("Skill adjustment for job {0} has zero adjustment value on: {1}",
                    adj.JobId, ctx);
            }
            return adj;
        }

        public void ApplyFor(JobSlot jobSlot, object ctx) {
            if (jobSlot.Incapable) {
                return;
            }
            jobSlot.AdjustLevel(Adjustment);
        }

        public void AddTooltip(StringBuilder sb, Jobs jobs) {
            if (jobs.TryJob(Job.IdHash, out var job))  {
                if (!job.Flags.HasFlag(JobAffinityFlags.Incapable)) {
                    sb.AppendFormat("\n{0} {1}",
                    Job.NameT, Texts.ColoredSignedNum(Adjustment));
                }
            }
        }
    }
}
