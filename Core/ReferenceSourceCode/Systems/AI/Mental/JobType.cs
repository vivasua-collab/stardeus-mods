using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Migrations;
using Game.Mods;
using Game.Systems.AI;
using Game.Systems.Backstories;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Game.Systems.Mental {
    [Serializable]
    public class JobType {

        [ExecuteOnReload]
        private static void ClearStatics() {
            // We want them to be fresh in unity
            All.Clear();
            Public.Clear();
            WithStatSources.Clear();
            tempJobTypes.Clear();
        }

        // load earlier because species and other stuff depends on jobs
        [SelfInit(SelfInit.Stage.Finalize, order: -10)]
        private static void Register() {
            LoadKnown();
        }
        public string Id;
        public int IdHash;
        // Dense 0..Count-1, set at load: the auction keeps per-job arrays instead of
        // dictionary lookups
        [NonSerialized] public int Index = -1;
        public static int Count => All.Count;
        public string IconId;
        private string nameT;
        public string NameT => nameT ??= $"job.type.{Id}".T();
        public string NameKey;
        public float Importance;
        public JobTypeFlags Flags;
        public bool Unskilled => (Flags & JobTypeFlags.Unskilled) == JobTypeFlags.Unskilled;
        public bool Internal => (Flags & JobTypeFlags.Internal) == JobTypeFlags.Internal;
        public bool NanobotCompatible => (Flags & JobTypeFlags.NanobotCompatible) == JobTypeFlags.NanobotCompatible;
        /// <summary>
        /// This is for JSON, use Flags instead in code
        /// </summary>
        [JsonEnum(typeof(Game.Systems.Mental.JobTypeFlags))]
        public string[] Types;
        /// <summary>
        /// If the being has these abilities, the job type will be added
        /// </summary>
        public string[] EnabledByAbilities;
        public int[] EnabledByAbilitiesH;
        /// <summary>
        /// If true, any abilities listed in EnabledByAbilities will be required
        /// </summary>
        public bool RequireAnyAbilities;
        /// <summary>
        /// Being stat that scales this job's work (WorkSpeed when not set). Manual jobs name
        /// ManualWorkSpeed, which Dexterity also drives.
        /// </summary>
        public string WorkSpeedStat;
        [NonSerialized] public int WorkSpeedStatH;
        // Skill level -> work speed for this job, a CurveDef id (the slope for that kind of work,
        // Config/Curves/Skills). Skills/SpeedDefault, a mild slope, when not set
        public string SkillSpeedCurve;
        private Curve skillSpeed;
        public Curve SkillSpeed => skillSpeed ??= LoadSkillSpeed();

        private Curve LoadSkillSpeed() {
            var curve = CurveDef.FastGet(Hashes.S(SkillSpeedCurve ?? CurveId.Skills_SpeedDefault));
            if (curve == null) {
                D.Err("Job {0} has a missing SkillSpeedCurve: {1}", Id, SkillSpeedCurve);
                curve = CurveDef.FastGet(CurveIdH.Skills_SpeedDefault);
            }
            return curve;
        }
        /// <summary>
        /// Being stats this job's skill level adds to (SkillStatSource)
        /// </summary>
        public JobStatSource[] ProvidesStats;

        public static readonly Dictionary<int, JobType> All = new();
        public static readonly Dictionary<int, JobType> Public = new();
        public static readonly List<JobType> WithStatSources = new();
        public static bool TryGet(string id, out JobType type) {
            if (All.TryGetValue(Hashes.S(id), out type)) {
                return true;
            }
            D.Err("Failed getting a JobType by id: {0}", id);
            return false;
        }
        public static bool TryGet(int id, out JobType type) {
            return All.TryGetValue(id, out type);
        }

        public static JobType Get(int type) {
            if (!All.TryGetValue(type, out var skill)) {
                D.Err("JobType not found by hash {0} ({1})! See Config/Jobs folder",
                    type, JobTypeIdH.ResolveName(type));
            }
            return skill;
        }

        public static void LoadKnown() {
            if (All.Count != 0) { return; }
            D.Warn("Loading job types...");

            var types = The.ModLoader.LoadObjects<JobType>("Config/Jobs");
            foreach (var s in types) {
                s.IdHash = Hashes.S(s.Id);
                s.Flags = Enums.FlagsFromStringArray_Ushort<JobTypeFlags>(
                    s.Types, s);
                if (s.NameKey != null) {
                    s.nameT = s.NameKey.T();
                }
                if (s.Importance == 0f && !s.Unskilled) {
                    D.Err("Importance undefined for job type: {0}. Setting to 1.",
                        s.Id);
                    s.Importance = 1f;
                }
                if (!Arrays.IsEmpty(s.EnabledByAbilities)) {
                    s.EnabledByAbilitiesH = new int[s.EnabledByAbilities.Length];
                    for (int i = 0; i < s.EnabledByAbilities.Length; i++) {
                        s.EnabledByAbilitiesH[i] = Hashes.S(s.EnabledByAbilities[i]);
                    }
                }
                if (s.Internal || s.Unskilled) {
                    s.WorkSpeedStatH = 0;
                } else {
                    s.WorkSpeedStatH = Hashes.S(s.WorkSpeedStat ?? StatId.WorkSpeed);
                }
                if (!Arrays.IsEmpty(s.ProvidesStats)) {
                    foreach (var src in s.ProvidesStats) {
                        src.StatIdH = Hashes.S(src.Stat);
                        src.CurveIdH = Hashes.S(src.CurveId);
                    }
                    WithStatSources.Add(s);
                }
                if (!All.TryAdd(s.IdHash, s)) {
                    D.Err("Duplicate job by id: {0}", s.Id);
                } else {
                    s.Index = All.Count - 1;
                    if (!s.Internal) {
                        Public.Add(s.IdHash, s);
                    }
                }
            }
            BeingRole.LoadKnown();
        }
        private static readonly HashSet<JobType> tempJobTypes = new();

        public static void ParseJobs(object ctx, string[] items,
                bool allowUnskilled,
                ref JobType[] jobs) {
            tempJobTypes.Clear();
            var category = JobTypeFlags.None;
            if (items == null) {
                jobs = null;
                return;
            }
            foreach (var j in items) {
                // try specific job first
                if (All.TryGetValue(Hashes.S(j), out var type)) {
                    if (!allowUnskilled && type.Unskilled) {
                        D.Err("Unskilled job listed in loved / hated jobs for {0}: {1}", ctx, type.Id);
                        continue;
                    }
                    if (type.Internal) {
                        D.Err("Internal job listed in loved / incapable jobs for {0}: {1}", ctx, type.Id);
                        continue;
                    }
                    tempJobTypes.Add(type);
                } else {
                    // try a job category
                    if (Enums.TryFromString<JobTypeFlags>(j, out var flag)) {
                        category |= flag;
                    } else {
                        D.Err("Invalid JobType or JobTypeFlags id in: {0}. Not found: {1}",
                            ctx, j);
                    }
                }
            }

            if (category != JobTypeFlags.None) {
                // filter out all the jobs in the category
                foreach (var j in All.Values) {
                    // Internal jobs (Task, Need, Idle) are invisible plumbing: the Unskilled
                    // category once made Cult Leaders incapable of Task, so they silently never
                    // took Task goals. Loves keep only skilled jobs, which can be loved.
                    if (j.Internal) { continue; }
                    if ((j.Flags & category) != JobTypeFlags.None) {
                        if (allowUnskilled || !j.Unskilled) {
                            // match!
                            tempJobTypes.Add(j);
                        }
                    }
                }
            }
            if (tempJobTypes.Count > 0) {
                #if UNITY_EDITOR
                if (tempJobTypes.Count > 7) {
                    D.Warn("Too many jobs in Role / Trait! {0} -> {1} | {2}",
                        tempJobTypes.Count, ctx, D.Dump(tempJobTypes));
                }
                #endif
                jobs = tempJobTypes.ToArray();
            }
        }
        public static void AppendJobList(StringBuilder sb, JobType[] jobTypes,
                Jobs jobs, bool skipIncapable, string label) {
            bool added = false;
            bool addedLabel = false;
            if (jobTypes != null) {
                for (int i = 0; i < jobTypes.Length; i++) {
                    JobType j = jobTypes[i];
                    if (jobs.TryJob(j.IdHash, out var slot)) {
                        if (skipIncapable && slot.Incapable) {
                            continue;
                        }
                        if (added) {
                            sb.Append(", ");
                        } else {
                            added = true;
                        }
                        if (!addedLabel) {
                            sb.AppendLine();
                            sb.Append(label);
                            sb.Append(" ");
                            addedLabel = true;
                        }
                        if (slot.Incapable) {
                            sb.Append(Texts.Red(j.NameT));
                        } else if (slot.Loves) {
                            sb.Append(Texts.Green(j.NameT));
                        } else {
                            sb.Append(j.NameT);
                        }
                    }
                }
            }
        }

        public override string ToString() {
            return $"Job[{Id}|{Flags}]";
        }
    }
}
