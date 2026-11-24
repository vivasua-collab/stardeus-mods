#if !PRODUCTION_BUILD
// #define DEBUG_JOB_GEN
#endif
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Mods;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Traits;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
using Game.CodeGen;
using Game.Constants;
namespace Game.Systems.Creatures {
    public sealed class GenBeingJobsDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingJobsDataStep());
        }
        public const string StepId = "Jobs";
        public override string Id => StepId;
        /// <summary>
        /// Mind has to be generated first
        /// </summary>
        public override int Order => 10;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            if (vars.BodyWithoutParts) {
                return true;
            }
            Jobs jobs = GenerateCoreJobs(data.Mind, rng);
            ApplyExperience(data, jobs);
            ApplyTraits(data, jobs);
            ApplyAge(data, jobs, rng);
            FixInconsistencies(rng, jobs);
            // Last on purpose, so the result matches what AfterAbilitiesChanged
            // produces on spawn: ability-driven slots start at level 1 / priority 3
            // and get their role and trait effects from SyncWithAbilities itself
            // (JobAffinities), but no age adjustments, and LacksAbilities never
            // reaches FixInconsistencies (which would zero out level and priority,
            // and that gets serialized).
            jobs.SyncWithAbilities(new GenBeingAbilities(data));
            return true;
        }

        public static void FixInconsistencies(Rng rng, Jobs jobs) {
            // fix any inconsistencies
            foreach (var jobSlot in jobs.JobSlots.Values) {
                if (jobSlot.Job.Unskilled) {
                    // Unskilled jobs cannot be loved
                    jobSlot.Flags &= ~JobAffinityFlags.Loves;
                } else {
                    // Modifiers drove this to 0
                    // if (jobSlot.Level == 0) {
                        // #if DEBUG_JOB_GEN
                        // D.Warn("Marking job as incapable due to level 0: {0} {1}", jobSlot.Job.NameT, data);
                        // #endif
                        // jobSlot.Flags |= JobAffinityFlags.Incapable;
                    // }
                }
                if (jobSlot.Incapable && jobSlot.Loves) {
                    if (rng.Chance(0.5f)) {
                        jobSlot.Flags &= ~JobAffinityFlags.Loves;
                    } else {
                        jobSlot.Flags &= ~JobAffinityFlags.Incapable;
                    }
                }
                if (jobSlot.Incapable) {
                    #if DEBUG_JOB_GEN
                    D.Warn("Removing incapable job: {0} {1}", jobSlot.Job.NameT, data);
                    #endif
                    jobSlot.Level = 0;
                    jobSlot.Priority = 0;
                }
            }
        }

        public static void ApplyTraits(BeingData data, Jobs jobs) {
            // Apply traits
            foreach (var trait in data.Mind.Traits.Slots) {
                // TODO deduplicate with roles above
                if (trait.Trait.AdjustsSkills != null) {
                    foreach (var adj in trait.Trait.AdjustsSkills) {
                        if (jobs.TryJob(adj.Job.IdHash, out var jobSlot)) {
                            adj.ApplyFor(jobSlot, trait.Trait);
                        }
                    }
                }
                var tr = trait.Trait;
                if (tr.LovesJobs != null) {
                    foreach (var job in tr.LovesJobs) {
                        if (jobs.TryJob(job.IdHash, out var jobSlot)) {
                            jobSlot.Flags |= JobAffinityFlags.Loves;
                        }
                    }
                }
                if (tr.IncapableJobs != null) {
                    foreach (var job in tr.IncapableJobs) {
                        if (jobs.TryJob(job.IdHash, out var jobSlot)) {
                            #if DEBUG_JOB_GEN
                            D.Warn("Marking job as incapable due to trait: {0} {1} {2}",
                                jobSlot.Job.NameT, data, tr);
                            #endif
                            jobSlot.Flags |= JobAffinityFlags.Incapable;
                        }
                    }
                }
                // end TODO
            }
        }

        public static void ApplyExperience(BeingData data, Jobs jobs) {
            // Apply experience
            foreach (var role in data.Mind.Experience.Roles.Values) {
                // TODO deduplicate with traits below
                if (role.AdjustsSkills != null) {
                    foreach (var adj in role.AdjustsSkills) {
                        if (jobs.TryJob(adj.Job.IdHash, out var jobSlot)) {
                            adj.ApplyFor(jobSlot, role);
                        }
                    }
                }
                if (role.LovesJobs != null) {
                    foreach (var job in role.LovesJobs) {
                        if (jobs.TryJob(job.IdHash, out var jobSlot)) {
                            jobSlot.Flags |= JobAffinityFlags.Loves;
                        }
                    }
                }
                if (role.IncapableJobs != null) {
                    foreach (var job in role.IncapableJobs) {
                        if (jobs.TryJob(job.IdHash, out var jobSlot)) {
                            #if DEBUG_JOB_GEN
                            D.Warn("Marking job as incapable due to role: {0} {1} {2}",
                                jobSlot.Job.NameT, data, role);
                            #endif
                            jobSlot.Flags |= JobAffinityFlags.Incapable;
                        }
                    }
                }
                // END TODO
            }
        }

        /// <summary>
        /// how many jobs (% of total) can be exceptional at this age percent
        /// </summary>
        private static readonly Curve pctExceptionalJobsAgeCurve = new float[] {
            0.0f, 0.03f,
            0.15f, 0.1f,
            0.25f, 0.15f,
            0.75f, 0.2f,
            1.0f, 0.15f,
        };
        private static readonly Curve exceptionalJobBonusCurve = new float[] {
            0.0f, 1f,
            0.1f, 1f,
            0.11f, 1f,
            0.5f, 2f,
            0.51f, 2f,
            0.89f, 2f,
            0.9f, 4f,
            1.0f, 5f,
        };
        private static readonly Dictionary<int, float> weightedSkills = new();
        private static Curve ageSkillFactor;
        private static Curve finalAdjustment;

        // The level so far (base roll, role and trait boosts, an exceptional bonus) is scaled by
        // age, then compressed above 10. The small additive gains per age phase it replaced left
        // most jobs at 0-2 and the best job at about 6; now a colonist has 2-4 jobs at 6-12
        // (Docs/Architecture/Plans/SkillParity.md).
        public static void ApplyAge(BeingData data, Jobs jobs, Rng rng) {
            var mind = data.Mind;
            if (!SpeciesJobs.TryFor(mind.Species, out var skills)) {
                return ;
            }
            float agePct = mind.Age / mind.Species.LifeExpectancyYears;

            float numExceptionalSkills = 0f;
            if (mind.Species.IsHumanoid) {
                numExceptionalSkills = pctExceptionalJobsAgeCurve.Evaluate(agePct);
                numExceptionalSkills = jobs.JobSlots.Count * numExceptionalSkills;
                numExceptionalSkills = Mathf.RoundToInt(numExceptionalSkills);
            }

            weightedSkills.Clear();
            for (int i = 0; i < skills.StartingJobs.Length; i++) {
                var jt = skills.StartingJobs[i];
                if (jt.Job == null) { continue; }
                if (jobs.TryJob(Hashes.S(jt.Job), out var job)) {
                    if (job.Incapable) { continue; }
                    float exceptionalChance = job.Level;
                    if (job.Loves && job.Level < 6) {
                        exceptionalChance *= 2f;
                    }
                    if (numExceptionalSkills > 0) {
                        weightedSkills.Add(job.Job.IdHash, exceptionalChance);
                    }
                }
            }

            while (numExceptionalSkills > 0 && weightedSkills.Count > 0) {
                numExceptionalSkills--;
                if (rng.TryWeightedFrom(weightedSkills, out int jobId)) {
                    if (jobs.TryJob(jobId, out var job)) {
                        // exceptional
                        var adj = rng.IntCurve(exceptionalJobBonusCurve);
                        var newLevel = Mathf.Clamp(job.Level + adj, 0, byte.MaxValue);
                        job.Level = (byte) newLevel;
                    }
                }
            }

            ageSkillFactor ??= CurveDef.FastGet(CurveIdH.Beings_AgeSkillFactor);
            finalAdjustment ??= CurveDef.FastGet(CurveIdH.Beings_SkillFinalAdjustment);
            // Only humanoids have a life story behind their skills; animals and bots start at
            // their species' levels
            float ageMax = mind.Species.IsHumanoid ? ageSkillFactor.Evaluate(agePct) : 1f;
            for (int i = 0; i < skills.StartingJobs.Length; i++) {
                var jt = skills.StartingJobs[i];
                if (jt.Job == null) { continue; }
                if (!jobs.TryJob(Hashes.S(jt.Job), out var job)) { continue; }
                if (job.Incapable || job.Job.Unskilled) { continue; }
                float level = job.Level;
                if (ageMax != 1f) {
                    level *= rng.Range(Mathf.Min(1f, ageMax), Mathf.Max(1f, ageMax));
                }
                level = finalAdjustment.Evaluate(level);
                job.Level = (byte) Mathf.Clamp(Mathf.RoundToInt(level), 0, JobSlot.SkillRangeTo);
                // Loves only came from roles and traits, 1.7 jobs per colonist, too few once
                // other jobs learn at x0.55. This roll adds about 2 more, mostly where the
                // colonist is good
                if (mind.Species.IsHumanoid && !job.Loves
                        && rng.Chance(job.Level * Tunables.SkillLovesChancePerLevel)) {
                    job.Flags |= JobAffinityFlags.Loves;
                }
            }
        }

        /// <param name="rng">If Rng is null, we will generate all jobs with level 1</param>
        public static Jobs GenerateCoreJobs(Mind mind, Rng rng) {
            Jobs jobs;
            if (mind.Jobs == null) {
                jobs = new Jobs();
                jobs.Owner = mind;
                mind.Jobs = jobs;
            } else {
                jobs = mind.Jobs;
            }

            if (!SpeciesJobs.TryFor(mind.Species, out var skills)) {
                return jobs;
            }


            for (int i = 0; i < skills.StartingJobs.Length; i++) {
                var jt = skills.StartingJobs[i];
                if (jt.Job == null) { continue; }
                byte level = 0;
                if (jt.JobType.Unskilled) {
                    level = 1;
                } else {
                    if (rng != null) {
                        level = (byte)math.round(jt.Curve.Evaluate(rng.Value));
                    }

                    #if DEBUG_JOB_GEN
                        D.Warn("Generated level for skill: {0}: {1}", jt.Job, level);
                    } else {
                        D.Warn("Generated level for skill: {0}: {1} (unskilled)", jt.Job, level);
                    #endif
                }
                jobs.AddJob(jt.JobType, level);
            }

            return jobs;
        }
    }
}
