using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;

namespace Game.Systems.Stats {
    // A job skill as a source of a being stat (JobType.ProvidesStats, e.g. Melee -> MeleeDodge),
    // so the stat tooltip names the skill like it names a body part or a trait.
    // It belongs to the being, not to the mind's JobSlot, and reads whatever mind is in the
    // body when the stat is recalculated: a mind transfer then only has to mark the stats
    // dirty (Refresh), instead of moving sources between bodies.
    public sealed class SkillStatSource : IStatSource {
        private readonly BeingData owner;
        private readonly JobType job;
        private readonly JobStatSource source;

        public string StatSourceNameT => job.NameT;

        private SkillStatSource(BeingData owner, JobType job, JobStatSource source) {
            this.owner = owner;
            this.job = job;
            this.source = source;
        }

        public static void AddAllTo(BeingData being) {
            var stats = being.StatsBeing;
            foreach (var job in JobType.WithStatSources) {
                foreach (var src in job.ProvidesStats) {
                    var stat = stats.AddStatSource(src.StatIdH,
                        new SkillStatSource(being, job, src));
                    if (stat == null) {
                        D.Err("Job {0} provides an unknown stat: {1}", job.Id, src.Stat);
                        continue;
                    }
                    stat.SourcesChanged();
                }
            }
        }

        // The skill level or the mind behind it changed
        public static void Refresh(BeingData being, JobType job) {
            if (being?.StatsBeing == null || job.ProvidesStats == null) { return; }
            foreach (var src in job.ProvidesStats) {
                if (being.StatsBeing.Find(src.StatIdH, out var stat)) {
                    stat.SourcesChanged();
                }
            }
        }

        public static void RefreshAll(BeingData being) {
            foreach (var job in JobType.WithStatSources) {
                Refresh(being, job);
            }
        }

        public bool GetEffectFor(Stat stat, out float toAdd, out float limit) {
            limit = 0f;
            toAdd = 0f;
            if (stat.Id != source.StatIdH) { return false; }
            var jobs = owner.Mind?.Jobs;
            if (jobs == null || !jobs.TryJob(job.IdHash, out var slot)) { return false; }
            toAdd = source.Curve.Evaluate(slot.Level);
            return toAdd != 0f;
        }

        public void UpdateStat(Stat stat) {
            if (GetEffectFor(stat, out var toAdd, out _)) {
                stat.UpdateBase(stat.BaseValue + toAdd);
            }
        }

        public override string ToString() {
            return $"SkillStatSource[{job.Id}->{source.Stat}]";
        }
    }
}
