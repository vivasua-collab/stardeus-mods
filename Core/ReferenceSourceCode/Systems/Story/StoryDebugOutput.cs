using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class StoryDebugOutput : IDebugOutputProvider {
        private readonly StoryContext ctx;
        private readonly GameState S;
        public StoryDebugOutput(StoryContext ctx) {
            this.ctx = ctx;
            S = ctx.S;
        }

        public string DebugPanelTitle => "Story System";
        private readonly StoryPickIgnore[] ignores = new StoryPickIgnore[] {
            StoryPickIgnore.None,
            StoryPickIgnore.Schedule,
            StoryPickIgnore.All,
        };

        public bool EveryFrame() {
            return false;
        }

        public bool OnTick(StringBuilder sb) {
            sb.Clear();
            var str = ctx.S.Query.ColonyCombatStrength.Ask();
            sb.AppendFormat("Colony Combat Strength: {0}\n", str);
            StoryEventOutcome nextSchedChange = StoryEventOutcome.Negative;
            long nextSchedChangeTick = long.MaxValue;
            foreach (var outc in StoryEventDef.AllOutcomes) {
                var outcome = (int) outc;
                var schedule = ctx.ScheduleFor(outc);

                var canSchedule = schedule.CanStartPeriodicEvent(
                    G.Ticks, out var isAllowed, out var isCooling);
                if (!isAllowed && schedule.ChangeAfter < nextSchedChangeTick) {
                    nextSchedChangeTick = schedule.ChangeAfter;
                    nextSchedChange = outc;
                }
            }
            // eligible story event counts this moment
            foreach (var outc in StoryEventDef.AllOutcomes) {
                var outcome = (int) outc;
                var schedule = ctx.ScheduleFor(outc);
                var canSchedule = schedule.CanStartPeriodicEvent(
                    G.Ticks, out var isAllowed, out var isCooling);
                string coolingText = string.Empty;
                if (isCooling) {
                    coolingText = Units.TicksToTime(schedule.CooldownUntil - G.Ticks);
                    coolingText = $" (Cooling: {coolingText})";
                    coolingText = Texts.Blue(coolingText);
                }
                sb.AppendFormat("{0}{1}: Change in: {2}",
                    isAllowed ? Texts.Green(outc.ToString()) : Texts.Red(outc.ToString()),
                    coolingText,
                    Texts.Accented2(Units.TicksToTime(schedule.ChangeAfter - G.Ticks), false));
                sb.AppendFormat(". Points: {0}/{1}",
                    Texts.Green(schedule.PointAllowance), Texts.Accented(schedule.MaxPointAllowance));
                if (nextSchedChange == outc && nextSchedChangeTick != long.MaxValue) {
                    sb.Append(Texts.Green(" <--- changes next"));
                }
                foreach (var ignore in ignores) {
                    int available = 0;
                    var handler = S.Query.StoryEventHandlerFor.Ask(outc);
                    var list = StoryEventDef.Considerable[outcome];
                    var highestW = 0f;
                    StoryEventDef highestWeightEvent = null;
                    foreach (var def in list) {
                        var w = handler.GetEventWeight(ctx, def, ignore);
                        if (w <= 0f) { continue; }
                        if (w > highestW) {
                            highestW = w;
                            highestWeightEvent = def;
                        }
                        available++;
                    }
                    if (highestWeightEvent != null) {
                        sb.AppendFormat("\nIgnore: {0}: {1}/{2} -> w {3}: {4}",
                            Texts.Accented2(ignore.ToString(), false),
                            Texts.Green(available), list.Count, Texts.Green(highestW.ToString("0.00")),
                            Texts.Accented(highestWeightEvent.Id, false));
                    } else {
                        sb.AppendFormat("\nIgnore: {0}: {1}/{2}",
                            Texts.Accented2(ignore.ToString(), false),
                            Texts.Green(available), list.Count);
                    }
                }
                sb.AppendLine();
            }

            sb.Append(Texts.Accented("Active Cooldowns: "));
            foreach (var flag in StoryEventDef.CoolableFlags) {
                if (ctx.IsCoolingDown(flag)) {
                    sb.AppendFormat("\n - {0} -> {1}", Texts.Blue(flag.ToString()),
                        Units.TicksToTime(ctx.FlagCooldowns[flag] - ctx.Ticks));
                }
            }
            if (ctx.ActiveHooks.Count > 0) {
                sb.AppendFormat("\n{0}: {1}", Texts.Accented("Active Hooks"), ctx.ActiveHooks.Count);
                foreach (var ev in ctx.ActiveHooks) {
                    sb.AppendFormat("\n - {0}", ev);
                }
            } else {
                sb.Append("\nNo active hooks");
            }

            if (ctx.OngoingEvents.Count > 0) {
                sb.AppendFormat("\n{0}: {1}", Texts.Accented("Ongoing"), ctx.OngoingEvents.Count);
                foreach (var ev in ctx.OngoingEvents) {
                    sb.AppendFormat("\n - {0}", DataLine(ev));
                }
            } else {
                sb.Append("\nNo ongoing events");
            }

            sb.AppendFormat("\n{0}: {1}", Texts.Accented("Past Events"), ctx.PastEvents.Count);
            for (int i = ctx.PastEvents.Count - 1; i >= Mathf.Max(0, ctx.PastEvents.Count - 5); i--) {
                StoryEventData ev = ctx.PastEvents[i];
                sb.AppendFormat("\n - {0}", DataLine(ev));
            }

            if (ctx.PrioritizedEvents.Count > 0) {
                sb.AppendFormat("\n{0}: {1}", Texts.Accented("Prioritized Events"),
                    ctx.PrioritizedEvents.Count);
                foreach (var ev in ctx.PrioritizedEvents) {
                    var def = StoryEventDef.FastGet(ev.Key);
                    if (def != null) {
                        sb.AppendFormat("\n - {0} ({1} ago)", def.Id,
                            Units.TicksToTime(G.Ticks - ev.Value));
                    } else {
                        sb.AppendFormat(Texts.Red("\n - UNKNOWN! {0}"), ev);
                        sb.AppendFormat(Texts.Red("\n - UNKNOWN ({0} ago)"),
                            Units.TicksToTime(G.Ticks - ev.Value));
                    }
                }
            }

            int availableOnDemand = 0;
            int totalOnDemand = 0;

            foreach (var ev in StoryEventDef.All.Values) {
                if (!ev.Flags.HasFlag(StoryEventFlags.OnDemand)) { continue; }
                totalOnDemand++;
                var handler = S.Query.StoryEventHandlerFor.Ask(ev.Outcome);
                var w = handler.GetEventWeight(ctx, ev, StoryPickIgnore.Schedule);
                if (w > 0f) {
                    availableOnDemand++;
                    // sb.AppendFormat("\n - {0}: {1}", Texts.Green(w.ToString("0.00")), ev.Id);
                }
            }
            sb.AppendFormat("\n{0}: {1}/{2}", Texts.Accented("Eligible Events (OnDemand)"),
                Texts.Green(availableOnDemand), totalOnDemand);

            sb.AppendFormat("\n{0}", Texts.Accented("Eligible Events (Considerable)"));
            foreach (var outc in StoryEventDef.AllOutcomes) {
                var handler = S.Query.StoryEventHandlerFor.Ask(outc);
                foreach (var ev in StoryEventDef.Considerable[(int) outc].OrderBy(e => e.Id)) {
                    var w = handler.GetEventWeight(ctx, ev, StoryPickIgnore.Schedule);
                    if (w > 0f) {
                        sb.AppendFormat("\n - {0}: {1} {2}", Texts.Green(w.ToString("0.00")), Texts.Grey(ev.OutcomeType), ev.Id);
                    }
                }
            }

            return true;
        }

        public static string DataLine(StoryEventData data) {
            if (data.EndTick == 0) {
                return string.Format("{0} [{1}]. Start: {2} ago. {3}",
                    Texts.Accented2(data.Def?.Id ?? "NoDef", false),
                    Texts.Green(data.State.ToString()),
                    Texts.Green(Units.TicksToTime(G.Ticks - data.StartTick)),
                    Texts.Red(data.FailReason));

            } else {
                return string.Format("{0} [{1}]. {2} - {3} ago. {4}",
                    Texts.Accented2(data.Def?.Id ?? "NoDef", false),
                    Texts.Yellow(data.State.ToString()),
                    Texts.Grey(Units.TicksToTime(G.Ticks - data.StartTick)),
                    Texts.Grey(Units.TicksToTime(G.Ticks - data.EndTick)),
                    Texts.Red(data.FailReason));

            }
        }
    }
}
