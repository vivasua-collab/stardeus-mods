using System.Collections.Generic;
using Game.Systems.Creatures;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Mental {
    public sealed class SpeciesJobs {
        private static readonly Dictionary<Species, SpeciesJobs> bySpecies = new();

        [ExecuteOnReload]
        private static void Init() {
            bySpecies.Clear();
        }

        public string Id;
        public Species BeingSpecies;
        public StartingSkillCurve[] StartingSkillCurves;
        public StartingSkillCurve[] AgeSkillCurves;
        public StartingJob[] StartingJobs;

        public static void LoadKnown() {
            if (bySpecies.Count != 0) { return; }
            var types = The.ModLoader.LoadObjects<SpeciesJobs>("Config/Species/Jobs");

            Dictionary<int, Curve> skillCurves = new();
            Dictionary<int, Curve> ageSkillCurves = new();
            Curve defaultSkillCurve = null;
            Curve defaultAgeSkillCurve = null;

            foreach (var s in types) {
                skillCurves.Clear();
                ageSkillCurves.Clear();
                s.BeingSpecies = Species.Get(s.Id);
                if (!bySpecies.TryAdd(s.BeingSpecies, s)) {
                    D.Err("SpeciesSkills with id {0} is already added!", s.Id);
                    continue;
                }

                skillCurves.Clear();
                defaultSkillCurve = null;
                for (int i = s.StartingSkillCurves.Length - 1; i >= 0; i--) {
                    StartingSkillCurve c = s.StartingSkillCurves[i];
                    if (c.Jobs == null) {
                        defaultSkillCurve = c.LevelCurve;
                    } else {
                        foreach (var j in c.Jobs) {
                            if (JobType.TryGet(j, out var job)) {
                                if (job.Flags.HasFlag(JobTypeFlags.Unskilled)) {
                                    D.Err("An unskilled job has a level curve for species: {0} -> {1}",
                                        s.Id, job.Id);
                                }
                                if (!skillCurves.TryAdd(job.IdHash, c.LevelCurve)) {
                                    D.Err("More than one level curve defined for a job {0} for species {1}",
                                        job.Id, s.Id);
                                }
                            } else {
                                D.Err("Misconfigured job in species: {0} -> {1}",
                                    s.BeingSpecies, j);
                            }
                        }
                    }
                }
                if (defaultSkillCurve == null) {
                    D.Err("Default skill curve not defined for species job: {0}", s.Id);
                }

                ageSkillCurves.Clear();
                defaultAgeSkillCurve = null;
                for (int i = s.AgeSkillCurves.Length - 1; i >= 0; i--) {
                    StartingSkillCurve c = s.AgeSkillCurves[i];
                    if (c.Jobs == null) {
                        defaultAgeSkillCurve = c.LevelCurve;
                    } else {
                        foreach (var j in c.Jobs) {
                            if (JobType.TryGet(j, out var job)) {
                                if (job.Flags.HasFlag(JobTypeFlags.Unskilled)) {
                                    D.Err("An unskilled job has a level age curve for species: {0} -> {1}",
                                        s.Id, job.Id);
                                }
                                if (!ageSkillCurves.TryAdd(job.IdHash, c.LevelCurve)) {
                                    D.Err("More than one level age curve defined for a job {0} for species {1}",
                                        job.Id, s.Id);
                                }
                            } else {
                                D.Err("Misconfigured job in species: {0} -> {1}",
                                    s.BeingSpecies, j);
                            }
                        }
                    }
                }

                for (int i = s.StartingJobs.Length - 1; i >= 0; i--) {
                    var sk = s.StartingJobs[i];
                    if (JobType.TryGet(sk.Job, out var job)) {
                        sk.JobType = job;
                    } else {
                        D.Err("Invalid job type in species skills: {0}.json -> {1}",
                            s.Id, sk.Job);
                    }
                    if (!job.Flags.HasFlag(JobTypeFlags.Unskilled)) {
                        if (skillCurves.TryGetValue(job.IdHash, out var curve)) {
                            sk.Curve = curve;
                        } else {
                            sk.Curve = defaultSkillCurve;
                        }
                        if (ageSkillCurves.TryGetValue(job.IdHash, out var ageCurve)) {
                            sk.AgeCurve = ageCurve;
                        } else {
                            sk.AgeCurve = defaultAgeSkillCurve;
                        }
                    }
                    s.StartingJobs[i] = sk;
                }
            }
        }

        public static bool TryFor(Species species, out SpeciesJobs skills) {
            return bySpecies.TryGetValue(species, out skills);
        }
        public override string ToString() {
            return $"SpeciesJobs[{Id}|{Arrays.Count(StartingJobs)}]";
        }
    }
}
