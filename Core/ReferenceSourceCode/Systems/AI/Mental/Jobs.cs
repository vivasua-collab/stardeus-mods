using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Scenarios;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Mental {
    public sealed class Jobs {
        public Mind Owner;
        public readonly Dictionary<int, JobSlot> JobSlots = new();
        /// <summary>
        /// Consider using CanDo instead! This returns a JobSlot even if the
        /// worker is incapable of doing it or has no priority for it!
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="job"></param>
        /// <returns></returns>
        public bool TryJob(int jobId, out JobSlot job) {
            return JobSlots.TryGetValue(jobId, out job);
        }
        public float SkillsValue {
            get {
                float value = 0f;
                foreach (var skill in JobSlots.Values) {
                    value += skill.SkillValue;
                }
                return value;
            }
        }

        public int VisibleCount {
            get {
                var cnt = 0;
                foreach (var j in JobSlots.Values) {
                    if (!j.IsInternal) {
                        cnt++;
                    }
                }
                return cnt;
            }
        }
        public bool CanHaveWeapon() {
            if (TryJob(JobTypeIdH.Melee, out var melee)) {
                if (!melee.Incapable) {
                    return true;
                }
            }
            if (TryJob(JobTypeIdH.Shooting, out var shooting)) {
                if (!shooting.Incapable) {
                    return true;
                }
            }
            return false;
        }

        public string Tooltip(string prepend = null, params int[] important) {
            if (Arrays.IsEmpty(JobSlots)) { return prepend; }
            var sb = Caches.StringBuilder3;
            sb.Clear();
            bool needNextBr = false;
            if (prepend != null) {
                sb.Append(prepend);
                needNextBr = true;
            }
            if (!Arrays.IsEmpty(important)) {
                needNextBr = AppendJobTips(sb, needNextBr, important, true);
            }
            needNextBr = AppendJobTips(sb, needNextBr, important, false);
            return sb.ToString();
        }

        private bool AppendJobTips(StringBuilder sb, bool needNextBr,
                int[] important, bool showImportant) {
            foreach (var skill in JobSlots.Values) {
                if (skill.Job.Internal) { continue; }
                bool isImportant = Arrays.ContainsSafe(important, skill.Job.IdHash);
                if (isImportant != showImportant) { continue; }
                if (needNextBr) {
                    sb.Append("\n");
                }
                needNextBr = true;
                if (showImportant) {
                    sb.Append(Texts.Accented(GlyphMap.TriangleRight));
                    sb.Append(' ');
                }
                if (skill.Level >= JobSlot.MaxLevel) {
                    sb.AppendFormat("{0}: {1:0.##} <b>{2}</b>",
                        skill.Job.NameT, skill.Level, T.MaxedOut);
                } else {
                    if (skill.Incapable) {
                        sb.Append(Texts.Red(Texts.Strike(skill.Job.NameT)));
                    } else {
                        var jobName = skill.Job.NameT;
                        if (skill.Loves) {
                            jobName = Texts.Green(jobName);
                        }
                        if (skill.Job.Unskilled) {
                            sb.AppendFormat("{0}: {1}",
                                jobName, Texts.GreenPlus);
                        } else {
                            string lvlUpProg;
                            if (skill.LevelProgress >= 0.01f) {
                                lvlUpProg = $" ({Units.Percentage(skill.LevelProgress)}{GlyphMap.ArrowRight}{Units.Num(skill.Level + 1)})";
                                lvlUpProg = Texts.Grey(lvlUpProg);
                            } else {
                                lvlUpProg = string.Empty;
                            }
                            sb.AppendFormat("{0}: {1:0.##}{2}",
                                jobName, skill.Level, lvlUpProg);
                        }
                    }
                }
            }

            return needNextBr;
        }

        public JobSlot AddJob(JobType job, byte level) {
            if (JobSlots.TryGetValue(job.IdHash, out var sl)) {
                D.Err("Trying to add a job type that already exists. {0} -> {1}",
                    Owner, job.Id);
                return sl;
            }
            var slot = new JobSlot(job);
            slot.Owner = Owner;
            slot.Level = level;
            slot.Priority = 3;
            // if (level < 1) {
            //     slot.Flags |= JobAffinityFlags.Incapable;
            //     slot.Priority = 0;
            // } else {
            //     slot.Priority = 3;
            // }
            JobSlots[job.IdHash] = slot;
            return slot;
        }

        public bool HasJobsWithFlags(JobTypeFlags flags) {
            foreach (var slot in JobSlots.Values) {
                if ((slot.Job.Flags & flags) == flags) {
                    return true;
                }
            }
            return false;
        }

        public static bool CanDo(AIAgentComp agent, int jobType) {
            if (agent == null) { return false; }
            return agent.Jobs.CanDo(jobType);
        }
        public bool CanDo(int id, out string because) {
            if (JobSlots.TryGetValue(id, out var slot)) {
                if (slot.Incapable) {
                    because = T.AdjIncapable;
                    return false;
                }
                if (slot.Priority == 0) {
                    because = T.BrainStatJobpriority0;
                    return false;
                }
                because = null;
                return true;
            }
            because = T.AdjIncapable;
            return false;
        }

        public bool CanDo(int id, out JobSlot slot) {
            if (JobSlots.TryGetValue(id, out slot)) {
                return slot.IsDoable;
            }
            return false;
        }
        public bool CanDo(int id) {
            if (JobSlots.TryGetValue(id, out var slot)) {
                return slot.IsDoable;
            }
            return false;
        }
        public bool CanDo(int id, out byte level) {
            if (JobSlots.TryGetValue(id, out var slot)) {
                level = slot.Level;
                return slot.IsDoable;
            }
            level = 0;
            return false;
        }

        // Feeds AISys's "Nobody can work on X jobs" check, so it has to agree with CanDo: a
        // robot that lost its hand must not count the colony as able to craft
        public void AppendDoable(HashSet<int> doableJobs) {
            foreach (var s in JobSlots) {
                if (s.Value.IsDoable) {
                    doableJobs.Add(s.Key);
                }
            }
        }

        public int LevelOf(int jobId) {
            if (JobSlots.TryGetValue(jobId, out var slot)) {
                return slot.Level;
            }
            return 0;
        }

        public void AbilitiesChanged(BeingData d) {
            if (SyncWithAbilities(d)) {
                A.S.Sig.BeingJobsChanged.Send(d);
            }
        }

        // Generic so the gen-time GenBeingAbilities struct is not boxed. True when a slot was
        // added.
        public bool SyncWithAbilities<T>(T abilities) where T : IAbilityHolder {
            bool added = false;
            // loop through all jobs and see if there are any jobs
            // that are enabled / disabled by the abilities
            foreach (var j in JobType.All.Values) {
                // this job type is static
                if (j.EnabledByAbilitiesH == null) { continue; }
                bool hasAllAbilities = true;
                for (int i = j.EnabledByAbilitiesH.Length - 1; i >= 0; i--) {
                    int abIdH = j.EnabledByAbilitiesH[i];
                    if (abilities.HasAbility(abIdH)) {
                        if (j.RequireAnyAbilities) {
                            hasAllAbilities = true;
                            break;
                        }
                    } else {
                        hasAllAbilities = false;
                        if (!j.RequireAnyAbilities) { break; }
                    }
                }
                if (JobSlots.TryGetValue(j.IdHash, out var slot)) {
                    // we have this slot already
                    slot.SetLacksAbilities(!hasAllAbilities);
                } else {
                    // no slot
                    if (hasAllAbilities) {
                        // add new slot!
                        // D.Warn("Adding a new job slot for being: {0}->{1}", j.Id, d);
                        var newSlot = AddJob(j, 1);
                        JobAffinities.ApplyToNewSlot(newSlot);
                        added = true;
                    }
                }
            }
            return added;
        }
        // A mind outside a body (a brain or core lying around, or in the ML Booth) lacks
        // nothing: the next body sets these again through SyncWithAbilities. Left as they
        // were, the booth showed the old body's limits until a reload (STA-1875)
        public void ClearLacksAbilities() {
            foreach (var slot in JobSlots.Values) {
                slot.SetLacksAbilities(false);
            }
        }

        public static Jobs BlankFor(Mind mind) {
            var jobs = new Jobs();
            jobs.SetOwner(mind);
            return jobs;
        }

        public void SetOwner(Mind mind) {
            Owner = mind;
        }

        public static JobSortOrder SortOrder {
            get {
                var order = CachedPlayerPrefs.GetInt("job.sort.order", (int) JobSortOrder.Alphabetic);
                return (JobSortOrder) order;
            }
            set {
                CachedPlayerPrefs.SetInt("job.sort.order", (int) value);
            }
        }
    }
}
