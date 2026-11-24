using System.Text;
using Game.Constants;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Collections;

namespace Game.Systems.Mental {
    // Generation applies roles and traits to the species starting job slots
    // (GenBeingJobsDataStep), but ability-driven slots are created by Jobs.SyncWithAbilities,
    // often long after that: a robot only gets Cleaning once it has a Manipulator, and a mind
    // moved into another body gets the jobs that body enables. Without this, the backstory
    // tooltip promised "Incapable of Cleaning" or "Cooking +3" that the job never got.
    public static class JobAffinities {
        // Called once, when the slot is created, so a later re-sync never stacks adjustments and
        // never brings back an Incapable that the ML Booth trained out.
        public static void ApplyToNewSlot(JobSlot slot) {
            var mind = slot.Owner;
            var roles = mind?.Experience?.Roles;
            if (roles != null) {
                foreach (var role in roles.Values) {
                    Apply(slot, role.AdjustsSkills, role.LovesJobs, role.IncapableJobs, role);
                }
            }
            var traits = mind?.Traits?.Slots;
            if (traits != null) {
                foreach (var ts in traits) {
                    var tr = ts.Trait;
                    Apply(slot, tr.AdjustsSkills, tr.LovesJobs, tr.IncapableJobs, tr);
                }
            }
            // Same outcome as GenBeingJobsDataStep.FixInconsistencies, minus its coin flip: there
            // is no generation rng here, so Incapable wins over Loves
            if (slot.Job.Unskilled) {
                slot.Flags &= ~JobAffinityFlags.Loves;
            }
            if ((slot.Flags & JobAffinityFlags.Incapable) == JobAffinityFlags.Incapable) {
                slot.Flags &= ~JobAffinityFlags.Loves;
                slot.Level = 0;
                slot.Priority = 0;
            }
        }

        // Reasons are worked out from the current mind when the tooltip is shown rather than
        // stored with the flag, so they cannot go stale. A flag with no current source (old
        // saves, EnsureIncapableOf) simply gets no reason line.
        public static void AppendIncapableReasons(JobSlot slot, StringBuilder sb) {
            if ((slot.Flags & JobAffinityFlags.Incapable) == JobAffinityFlags.Incapable) {
                AppendSources(slot, sb, loves: false);
            }
            if (slot.LacksAbilities) {
                AppendReasonPrefix(sb);
                BeingUtils.AppendLackingAbilities(sb, slot.Owner?.Owner,
                    FrugalArray<int>.FromArray(slot.Job.EnabledByAbilitiesH));
            }
        }

        public static void AppendLovesReasons(JobSlot slot, StringBuilder sb) {
            AppendSources(slot, sb, loves: true);
        }

        // A job that any one of its abilities enables (Cleaning: Clean Device or Clean Floor)
        // looks fully capable on a being with only some of them. A robot's hand cleans devices
        // but not floors, so its Cleaning job shows a skill while dirty rooms stay dirty.
        public static bool HasPartialAbilities(JobSlot slot) {
            var job = slot.Job;
            var abilities = job.EnabledByAbilitiesH;
            if (!job.RequireAnyAbilities || abilities == null || abilities.Length < 2
                    || slot.LacksAbilities) {
                return false;
            }
            var being = slot.Owner?.Owner;
            if (being == null) { return false; }
            for (int i = 0; i < abilities.Length; i++) {
                if (!being.HasAbility(abilities[i])) { return true; }
            }
            return false;
        }

        // One "Ability: Yes / Incapable" line per ability, without a trailing line break
        public static void AppendPartialAbilities(JobSlot slot, StringBuilder sb) {
            var being = slot.Owner.Owner;
            var abilities = slot.Job.EnabledByAbilitiesH;
            for (int i = 0; i < abilities.Length; i++) {
                if (i > 0) {
                    sb.Append('\n');
                }
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.Append(Ability.Get(abilities[i]).NameT);
                sb.Append(": ");
                sb.Append(being.HasAbility(abilities[i]) ? T.Yes : Texts.Red(T.AdjIncapable));
            }
        }

        public static string WithPartialAbilities(JobSlot slot, string tip) {
            if (!HasPartialAbilities(slot)) { return tip; }
            tipBuilder.Clear();
            if (tip != null) {
                tipBuilder.Append(tip);
                tipBuilder.Append('\n');
            }
            AppendPartialAbilities(slot, tipBuilder);
            return tipBuilder.ToString();
        }

        public static string IncapableTip(JobSlot slot) {
            tipBuilder.Clear();
            tipBuilder.Append(Texts.Red(T.AdjIncapable));
            AppendIncapableReasons(slot, tipBuilder);
            return tipBuilder.ToString();
        }

        public static string LovesTip(JobSlot slot) {
            tipBuilder.Clear();
            tipBuilder.Append(Texts.Green(T.VerbLoves));
            AppendLovesReasons(slot, tipBuilder);
            return tipBuilder.ToString();
        }

        // Not one of the shared Caches builders: JobSlot.GetNameAndLevelText runs inside other
        // tooltip code (e.g. the weapon slot tip) that may be holding those
        private static readonly StringBuilder tipBuilder = new();

        private static void AppendSources(JobSlot slot, StringBuilder sb, bool loves) {
            var job = slot.Job;
            var mind = slot.Owner;
            var roles = mind?.Experience?.Roles;
            if (roles != null) {
                foreach (var role in roles.Values) {
                    if (Contains(loves ? role.LovesJobs : role.IncapableJobs, job)) {
                        AppendReasonPrefix(sb);
                        sb.Append(T.PersonaLogBackstory_V1.F(role.NameT));
                    }
                }
            }
            var traits = mind?.Traits?.Slots;
            if (traits != null) {
                foreach (var ts in traits) {
                    var tr = ts.Trait;
                    if (Contains(loves ? tr.LovesJobs : tr.IncapableJobs, job)) {
                        AppendReasonPrefix(sb);
                        sb.Append($"{T.NounTrait}: {tr.NameT}");
                    }
                }
            }
        }

        public static bool Contains(JobType[] jobs, JobType job) {
            if (jobs == null) { return false; }
            for (int i = 0; i < jobs.Length; i++) {
                if (jobs[i].IdHash == job.IdHash) { return true; }
            }
            return false;
        }

        private static void Apply(JobSlot slot, SkillAdjustment[] adjusts, JobType[] loves,
                JobType[] incapable, object ctx) {
            if (adjusts != null) {
                foreach (var adj in adjusts) {
                    if (adj.Job != null && adj.Job.IdHash == slot.Job.IdHash) {
                        adj.ApplyFor(slot, ctx);
                    }
                }
            }
            if (Contains(loves, slot.Job)) {
                slot.Flags |= JobAffinityFlags.Loves;
            }
            if (Contains(incapable, slot.Job)) {
                slot.Flags |= JobAffinityFlags.Incapable;
            }
        }

        private static void AppendReasonPrefix(StringBuilder sb) {
            sb.Append("\n ");
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
        }
    }
}
