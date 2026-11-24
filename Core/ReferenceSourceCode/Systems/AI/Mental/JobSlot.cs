#if !PRODUCTION_BUILD
// #define DEBUG_COST
// #define DEBUG_JOBS
#endif
#if UNITY_EDITOR
// #define VERBOSE_LEVEL_UP
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.AI;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems.Stats;

namespace Game.Systems.Mental {
    public sealed class JobSlot {
        public const int SkillRangeTo = 20;
        // Training goes past the generated range (SkillRangeTo) up to here, no further. Levels
        // up to 255 made the top of every skill curve meaningless.
        public const int MaxLevel = 25;
        // 0 = off, 1..5 with 5 the most preferred (Docs/Architecture/Systems/AI/JobPriorities.md)
        public const byte MaxPriority = 5;
        public const byte DefaultPriority = 3;
        public Mind Owner;
        public readonly JobType Job;
        public byte Priority;
        private BCallback changedB;
        public BCallback ChangedB => changedB ??= new BCallback();
        private byte level;
        public byte Level {
            get => level;
            set {
                if (level == value) { return; }
                #if DEBUG_JOBS
                D.SetFieldWithDebug(ref level, value, $"JobLevel[{Job.NameT}");
                #else
                level = value;
                #endif
                // the Job Matrix and the Jobs subpanel show the level
                changedB?.Invoke();
                // a skill can be a stat source (JobType.ProvidesStats): Melee -> MeleeDodge
                // not while loading: Owner is still unset, and Mind.SetOwner refreshes them
                var being = Owner?.Owner;
                if (Job.ProvidesStats != null && being != null && being.Mind == Owner) {
                    SkillStatSource.Refresh(being, Job);
                }
            }
        }
        public float LevelProgress;
        // XP gained since midnight, for the daily soft cap (SkillUpkeepSys resets it)
        public float XpToday;
        public bool IsInternal;
        public float ActionCost(long ticks) {
            if (Incapable) {
                D.Err("Trying to get ActionCost for job slot when worker is incapable! {0}", this);
                return 1000000f;
            }
            if (Priority < 1) {
                return 1000000f;
            }
            var priorityCost = 6000f - Priority * 1000f;
            if ((Job.Flags & JobTypeFlags.Unskilled) == JobTypeFlags.Unskilled) {
                return priorityCost;
            }
            var cost = priorityCost - Level * 10f;
            #if DEBUG_COST
            D.Warn("Action cost for job {0} with skill {1} prio {2}: {3}",
                Job.NameT, Level, Priority, cost);
            #endif
            return cost;
        }
        public JobSlot(JobType job) {
            Job = job;
            IsInternal = (job.Flags & JobTypeFlags.Internal) == JobTypeFlags.Internal;
        }
        public JobAffinityFlags Flags;
        public bool LacksAbilities;

        // At runtime, so the job shows as incapable (or capable again) wherever it is shown
        public void SetLacksAbilities(bool lacks) {
            if (LacksAbilities == lacks) { return; }
            LacksAbilities = lacks;
            changedB?.Invoke();
        }
        /// <summary>
        /// To get the efficiency + level the skill up, use UseSkill instead!
        /// <see cref="UseSkill(float)"/>
        /// </summary>
        // The job's own slope (JobType.SkillSpeed), anchored so a typical colonist works at
        // speed 1. It was sqrt(level) for every job: experts 4.5x faster at research and
        // cleaning alike, and Loves another x1.5, which made a loved job a work speed perk
        // instead of a learning one.
        private float efficiency => Job.Unskilled ? 1f : Job.SkillSpeed.Evaluate(Level);
        /// <summary>
        /// Levels skill up upon use. xp is the XP for this one use (SkillXp has the
        /// per-tick rates); Loves and the daily cap apply in TrainSkill.
        /// </summary>
        /// <returns>Current skill efficiency</returns>
        public float UseSkill(float xp = SkillXp.Default) {
            if (IsInternal) {
                return 1f;
            }
            if (LacksAbilities) {
                D.Err("Trying to use a skill when we lack abilities: {0} | {1}", Job, Owner);
                return 0f;
            }
            if ((Flags & JobAffinityFlags.Incapable) == JobAffinityFlags.Incapable) {
                D.Err("Trying to use a skill that are incapable of! {0} | {1}",
                    Job, Owner);
                return 0f;
            }
            TrainSkill(xp, false);
            #if VERBOSE_LEVEL_UP
            D.Warn("Using skill {0} (lvl: {1}). Work amount: {2}",
                Job.Id, Level, efficiency);
            #endif
            return efficiency;
        }

        // UseSkill times the being's work speed stat (hands, eyes, light)
        public float UseSkillAtWorkSpeed(float xp) {
            var eff = UseSkill(xp);
            if (eff <= 0f) {
                return 0f;
            }
            return eff * WorkSpeedMul();
        }

        // The being's work speed stat for this job (JobType.WorkSpeedStat): darkness, eyes
        // and, for manual jobs, hands. 1 without a body to judge, e.g. in the ML booth.
        public float WorkSpeedMul() {
            var statId = Job.WorkSpeedStatH;
            if (statId == 0) { return 1f; }
            var being = Owner?.Owner;
            if (being?.StatsBeing == null || being.Mind != Owner) { return 1f; }
            if (!being.StatsBeing.Find(statId, out var stat)) { return 1f; }
            return stat.Value;
        }

        private static Curve xpToNextLevel;

        // Skills/XpToNextLevel: 1000 XP for the first level, 30000 for the 20th
        public static float XpToNextLevel(int level) {
            xpToNextLevel ??= CurveDef.FastGet(CurveIdH.Skills_XpToNextLevel);
            return xpToNextLevel.Evaluate(level);
        }

        // isTraining: the ML booth, which writes skill straight into the mind: no passion factor
        // and no daily cap
        public void TrainSkill(float xp, bool isTraining) {
            xp *= Tunables.SkillLevelUpSpeed;
            if (!isTraining) {
                xp *= Loves ? Tunables.SkillLearnMulLoves : Tunables.SkillLearnMulNone;
                if (XpToday >= Tunables.SkillDailySoftCap) {
                    xp *= Tunables.SkillSaturatedLearnMul;
                }
                XpToday += xp;
            }
            LevelProgress += xp / XpToNextLevel(Level);
            #if VERBOSE_LEVEL_UP
            D.Warn("Leveling up {0}. Lvl: {1}. XP: {2} Progress: {3:0.0000}",
                Job.Id, Level, xp, LevelProgress);
            #endif
            while (LevelProgress >= 1f && Level < MaxLevel) {
                if (isTraining && Level == 0) {
                    Flags &= ~JobAffinityFlags.Incapable;
                    Priority = 3;
                }
                // the XP past this level carries over, measured against the next one
                var carried = (LevelProgress - 1f) * XpToNextLevel(Level);
                Level++;
                LevelProgress = carried / XpToNextLevel(Level);
                NotifyLevelUp();
                if (Job.Unskilled && Level > 1) {
                    D.Err("Unskilled job has level > 1! {0} | {1}", this, Job);
                    Level = 1;
                    LevelProgress = 0f;
                    return;
                }
            }
            // At the cap the progress is a reserve that decay eats first. Without it the cap had
            // nothing to lose, and the first hour of decay took the level whatever the training
            if (Level >= MaxLevel && LevelProgress > 1f) {
                LevelProgress = 1f;
            }
        }

        // LevelProgress at the cap is the reserve, not progress toward a level 26
        public float ShownProgress => Level >= MaxLevel ? 0f : LevelProgress;

        // Skill decay (SkillUpkeepSys): the XP comes off the progress toward the next level,
        // and a level is lost when that runs out
        public void Forget(float xp) {
            if (xp <= 0f || Level == 0) { return; }
            LevelProgress -= xp / XpToNextLevel(Level);
            while (LevelProgress < 0f && Level > 0) {
                var missing = -LevelProgress * XpToNextLevel(Level);
                Level--;
                LevelProgress = 1f - missing / XpToNextLevel(Level);
            }
            if (LevelProgress < 0f) {
                LevelProgress = 0f;
            }
        }

        private void NotifyLevelUp() {
            // owner.owner (being data) may be null if it's in ML booth, be careful!
            if (!Owner.Faction.IsPlayer) {
                if (Owner.Owner?.Body.IsForcedToObey != true) {
                    return;
                }
            }
            if (Owner.Owner != null) {
                var msg = T.SkillUp_V3.F(string.Empty, Job.NameT, Level).TrimStart();
                A.S.Sys.Log.AddLine(Owner.Owner, Texts.Green(msg));
            } else {
                var msg = T.SkillUp_V3.F(Owner.LinkToShortName, Job.NameT, Level);
                A.S.Sys.Log.AddLine(Texts.Green(msg));
            }
        }

        public float SkillEfficiency() {
            if (Incapable) { return 0f; }
            return efficiency;
        }

        public bool Incapable => LacksAbilities || (Flags & JobAffinityFlags.Incapable) == JobAffinityFlags.Incapable;
        // The one rule for "will take this work". Priority alone is not enough: an incapable slot
        // is normally zeroed, but an old save or a mod can leave it above 0, and losing a hand
        // keeps the priority so the job comes back with the hand.
        public bool IsDoable => !Incapable && Priority > 0;
        // An unskilled job can never be loved, whatever the flags say (mods, saves)
        public bool Loves => !Job.Unskilled
            && (Flags & JobAffinityFlags.Loves) == JobAffinityFlags.Loves;
        public void AdjustLevel(sbyte adj) {
            int adjusted = Level + adj;
            if (adj < 0) {
                // if (Level == 0) { return; }
                if (adjusted < 1) {
                    // let's not go below 1 ever
                    Level = 1;
                    return;
                }
            }
            Level = (byte) Maths.Clamp(adjusted, 0, MaxLevel);
        }
        public float SkillValue {
            get {
                if (Incapable) { return 0f; }
                return (1 + Level) * Job.Importance;
            }
        }

        public int SkillOrder {
            get {
                if ((Flags & JobAffinityFlags.Incapable) == JobAffinityFlags.Incapable) {
                    return -10;
                }
                if ((Job.Flags & JobTypeFlags.Unskilled) == JobTypeFlags.Unskilled) {
                    return -1;
                }
                return Level;
            }
        }

        public string JobTooltip {
            get {
                jobName = Job.NameT;
                string lvlText;
                if (Incapable) {
                    lvlText = Texts.RedMinus;
                    jobName = Texts.Red(jobName);
                } else {
                    if (Loves) {
                        jobName = Texts.Green(jobName);
                    }
                    if (Job.Unskilled) {
                        lvlText = Texts.GreenPlus;
                    } else {
                        lvlText = Texts.Accented(Units.Num(Level));
                    }
                }
                return $"{jobName}: {lvlText}";
            }
        }

        private string jobName;

        public void ToggleJob() {
            if (Incapable) {
                UISounds.PlayActionDenied();
                changedB?.Invoke();
                return;
            }
            SetPriority(Priority == 0 ? DefaultPriority : (byte) 0);
        }

        // dir +1 raises, -1 lowers, both wrap through off: 3 4 5 off 1 2 3
        public void CyclePriority(int dir) {
            if (Incapable) {
                UISounds.PlayActionDenied();
                changedB?.Invoke();
                return;
            }
            SetPriority(NextPriority(Priority, dir));
        }

        public static byte NextPriority(byte current, int dir) {
            int next = (current + (dir < 0 ? -1 : 1)) % (MaxPriority + 1);
            if (next < 0) { next += MaxPriority + 1; }
            return (byte) next;
        }

        public void SetPriority(byte priority) {
            if (priority > MaxPriority) { priority = MaxPriority; }
            bool wasOn = Priority > 0;
            Priority = priority;
            changedB?.Invoke();
            if (wasOn != (priority > 0)
                    && (Job.IdHash == JobTypeIdH.Shooting || Job.IdHash == JobTypeIdH.Melee)) {
                Owner?.Owner?.Body.ValidateAttackMethods();
            }
        }

        public string Tooltip() {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendLine(Texts.Accented(Job.NameT));
            if (Incapable) {
                sb.Append(Texts.Red(T.AdjIncapable));
                JobAffinities.AppendIncapableReasons(this, sb);
                sb.AppendLine();
                return sb.ToString();
            }
            if (Loves) {
                sb.Append(T.VerbLoves);
                JobAffinities.AppendLovesReasons(this, sb);
                sb.AppendLine();
            }
            if (JobAffinities.HasPartialAbilities(this)) {
                JobAffinities.AppendPartialAbilities(this, sb);
                sb.AppendLine();
            }

            if (!Job.Unskilled && Level < MaxLevel) {
                var progress = Units.PercentageWithFraction2(LevelProgress);
                sb.AppendLine(T.JobLevelProgress_V1.F(progress));
                // past the daily soft cap the skill learns at a fifth of the rate; without this
                // line the slowdown looks like a bug
                if (XpToday >= Tunables.SkillDailySoftCap) {
                    sb.AppendLine(Texts.Grey(T.JobLearningSaturated));
                }
            }
            return sb.ToString();
        }


        public void GetNameAndLevelText(out string jobName, out string lvlText, out string tip) {
            jobName = Job.NameT;
            // tip = Texts.Accented(jobName);
            tip = null;
            if (Incapable) {
                lvlText = GlyphMap.SpriteCross;
                jobName = Texts.Red(jobName);
                tip = JobAffinities.IncapableTip(this);
            } else {
                if (Loves) {
                    jobName = Texts.Green(jobName);
                    tip = JobAffinities.LovesTip(this);
                }
                if (Job.Unskilled) {
                    lvlText = GlyphMap.SpriteCheck;
                    tip = T.UnskilledJobTip;
                } else {
                    // lvlText = T.JobLevel_V1.F(Level);
                    lvlText = Units.Num(Level);
                }
                tip = JobAffinities.WithPartialAbilities(this, tip);
            }
        }


        public override string ToString() {
            return $"JobSlot[{Job}|{Level}|{Flags}|{Owner}]";
        }
    }
}
