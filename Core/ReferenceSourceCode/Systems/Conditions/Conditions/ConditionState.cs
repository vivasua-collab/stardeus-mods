using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.Conditions {
    public class ConditionState {
        public Condition Condition;
        public Severity Severity;
        public BeingData Owner;
        public ConditionFlags Flags;
        public float LastDelta;
        public long StartTick;
        /// <summary>
        /// For conditions that will end after X ticks.
        /// <seealso cref="TicksInCondition()"/>
        /// </summary>
        public int Duration;
        public int TicksInCondition(long ticksNow) {
            #if !PRODUCTION_BUILD
            if (StartTick == 0) {
                D.Err("Condition start tick is 0! {0}", this);
            }
            #endif
            return (int) (ticksNow - StartTick);
        }
        public bool IsRegistered => (Flags & ConditionFlags.RegisteredWithSystem) == ConditionFlags.RegisteredWithSystem;
        public bool IsInactive => (Flags & ConditionFlags.Inactive) == ConditionFlags.Inactive;
        public bool IsDisabled => IsInactive ||
            (((Flags & ConditionFlags.PauseWhenBeingHidden) == ConditionFlags.PauseWhenBeingHidden)
                && Owner.HasStateFlag(BeingState.Hidden));
        public bool IsUndetected => (Flags & ConditionFlags.Undetected) == ConditionFlags.Undetected;
        public bool TickAfterDeath => (Flags & ConditionFlags.TickAfterDeath) == ConditionFlags.TickAfterDeath;
        public void SetSeverity(Severity s) {
            if (Severity == s) { return; }
            Severity = s;
        }

        public static bool CreateFor(GameState S, int condIdH,
                BeingData data, Severity sev, bool ignoreExisting,
                bool allowInStasis = false) {
            if (data.IsInStasis && !allowInStasis) {
                // Whoever picked this person did not skip sleepers. Nothing may decide to
                // afflict someone frozen in a pod - every wake-up path clears
                // ColonyRole.InStasis before it spawns the body (StasisUtils,
                // ManifestSys.DieInPod). The one exception is the body's own record of
                // wounds, see Body.CheckCondition.
                D.Err("Trying to create a condition on a being in stasis: {0}: {1}",
                    ConditionIdH.ResolveName(condIdH), data);
                return false;
            }
            if (data.HasCondition(condIdH)) {
                if (!ignoreExisting) {
                    D.Err("Trying to create duplicate condition: {0}: {1}",
                        ConditionIdH.ResolveName(condIdH), data);
                }
                return false;
            }
            var cnd = Registries.Conditions.FastGet(condIdH);
            if (cnd == null) {
                D.Err("Could not find {0} ({1}) condition!", condIdH,
                    ConditionIdH.ResolveName(condIdH));
                return false;
            }
            var condState = new ConditionState() {
                Condition = cnd,
                Severity = sev
            };
            data.AddCondition(S, condState);
            return true;
        }


        public string Tooltip(bool full) {
            var sb = Caches.StringBuilder2;
            var ticks = The.R.State.Ticks;
            var hasFor = ticks - StartTick;
            sb.Clear();
            bool needsBR = false;
            if (full) {
                var desc = Condition.DescT;
                if (desc != null) {
                    sb.Append(Condition.DescT);
                    needsBR = true;
                }
            }
            if (StartTick != 0) {
                if (needsBR) {
                    needsBR = false;
                    sb.Append(GlyphMap.BRHR);
                } else {
                    needsBR = true;
                }
                sb.AppendLine(T.ConditionDuration_V1.F(Units.TicksToTime(hasFor)));
            }
            if (Duration != 0) {
                if (needsBR) {
                    needsBR = false;
                    sb.Append(GlyphMap.BRHR);
                } else {
                    needsBR = true;
                }
                var endsIn = Duration - hasFor;
                sb.AppendLine(T.EndsIn_V1.F(Units.TicksToTime(endsIn)));
            }
            if (Severity < Severity.Irreparable) {
                if (Owner.ConditionProgress.TryGetValue(Condition.IdH, out var prog)) {
                    if (prog > 0.01f || !Condition.IsPositive) {
                        if (LastDelta < 0f) {
                            var sevAfter = Severity - 1;
                            sb.Append(Severities.NameFor(Severity, Condition.IsPositive));
                            sb.Append(' ');
                            sb.Append(GlyphMap.ArrowRight);
                            sb.Append(' ');
                            sb.Append(Texts.Accented(Units.Percentage(prog)));
                            sb.Append(GlyphMap.ArrowRight);
                            sb.Append(' ');
                            sb.Append(Severities.NameFor(sevAfter, Condition.IsPositive));
                            sb.Append(' ');
                            sb.Append(Texts.GreyParentheses(T.StateDecreasing));
                            sb.AppendLine();
                        } else {
                            var sevAfter = Severity + 1;
                            sb.Append(Severities.NameFor(Severity, Condition.IsPositive));
                            sb.Append(' ');
                            sb.Append(GlyphMap.ArrowRight);
                            sb.Append(' ');
                            sb.Append(Texts.Accented(Units.Percentage(prog)));
                            sb.Append(' ');
                            sb.Append(GlyphMap.ArrowRight);
                            sb.Append(' ');
                            sb.Append(Severities.NameFor(sevAfter, Condition.IsPositive));
                            if (LastDelta > 0f) {
                                sb.Append(' ');
                                sb.Append(Texts.GreyParentheses(T.StateIncreasing));
                            }
                            sb.AppendLine();
                        }
                    }
                }
            // } else {
            //     return D.DumpInstance(this);
            }
// #if !PRODUCTION_BUILD
//             sb.AppendFormat("\nIsRegistered: {0}", IsRegistered);
//             sb.AppendFormat("\nDisabled: {0}", IsDisabled);
//             sb.AppendFormat("\nFlags: {0}", Flags);
// #endif
            return sb.ToString();
        }
        public override string ToString() {
            if (Duration > 0) {
                return $"Condition[{Condition.Id}/{Severity}/{Units.TicksToTime(Duration)}->{Owner}]";
            } else {
                return $"Condition[{Condition.Id}/{Severity}->{Owner}]";
            }
        }
        public static float GetProgress(BeingData data, int condId) {
            if (data.ConditionProgress.TryGetValue(condId, out var prog)) {
                return prog;
            }
            return 0f;
        }

        public static void SetProgress(BeingData data,
                int condId, float level) {
            data.ConditionProgress[condId] = level;
            data.NotifyConditionsListChanged();
            data.NotifyDevelopingConditionsChanged();
        }
    }
}
