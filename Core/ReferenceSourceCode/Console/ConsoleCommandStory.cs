#if !STARDEUS_DEMO
using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using System.Text;
using Game.Data.Economy;
using KL.Randomness;
using System.Linq;
using Game.Story.Events;
using Game.UI;
using Game.Modifiers;
using Game.Utils;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Systems.AI.Combat;

namespace Game.Console {
    public class ConsoleCommandStory : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandStory());
        }
        protected override Dictionary<string, string> subCommands => new() {
            { "find", "Find" },
            { "list", "List" },
            { "available", "Available" },
            { "disable", "Disable" },
            { "disabled", "Disabled" },
            { "enable", "Enable" },
            { "check", "Check" },
            { "ongoing", "Ongoing" },
            { "end_ongoing", "EndOngoing" },
            { "trigger", "Trigger" },
            { "schedule", "Schedule" },
            { "cooldown_clear", "CooldownClear" },
            { "cooldown_list", "CooldownList" },
            { "debug", "ShowDebug" },
            { "report", "Report" },
            { "points_report", "PointsReport" },
            { "raid_report", "RaidReport" },
            { "raid_test", "RaidTest" },
            { "adapt", "Adapt" },
        };
        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "list",
            "available",
            "find",
            "check",
            "disable",
            "disabled",
            "ongoing",
            "enable",
            "cooldown_list",
            "debug",
            "points_report",
            // "report",
            // "raid_report",
        };
        public override string Autocomplete(ConsoleCommandArguments args) {
            var cmd = args.GetString(1);
            if (cmd != "enable" && cmd != "disable" && cmd != "trigger" && cmd != "check"
                    && cmd != "raid_report" && cmd != "raid_test") {
                return base.Autocomplete(args);
            }
            var id = args.GetString(2);
            if (id == null) { return null; }
            id = id.ToLowerInvariant();
            foreach (var evt in StoryEventDef.Ordered) {
                if (evt.Id.ToLowerInvariant().StartsWith(id)) {
                    return $"{cmd} {evt.Id}";
                }
            }
            foreach (var evt in StoryEventDef.Ordered) {
                if (evt.Id.ToLowerInvariant().Contains(id)) {
                    return $"{cmd} {evt.Id}";
                }
            }
            return base.Autocomplete(args);
        }
        protected ConsoleCommandResult ShowDebug(ConsoleCommandArguments args) {
            var ctx = The.R.Query.GetStoryContext.Ask();
            DebugOutputWidget.SpawnFor(new StoryDebugOutput(ctx));
            return Close();
        }
        protected ConsoleCommandResult Report(ConsoleCommandArguments args) {
            var ctx = The.R.Query.GetStoryContext.Ask();
            var sb = Caches.StringBuilder;
            sb.Clear();

            // --- Schedule states ---
            sb.Append("<b>Schedules:</b>");
            foreach (var outc in StoryEventDef.AllOutcomes) {
                var sch = ctx.ScheduleFor(outc);
                sch.CanStartPeriodicEvent(G.Ticks, out var isAllowed, out var isCooling);
                var status = isAllowed ? "ON" : "OFF";
                var flipIn = sch.ChangeAfter > G.Ticks
                    ? Units.TicksToTime(sch.ChangeAfter - G.Ticks)
                    : "now";
                sb.AppendFormat("\n  {0}: {1}, flips in {2}, pts {3}/{4}",
                    outc, status, flipIn, sch.PointAllowance, sch.MaxPointAllowance);
                if (isCooling) {
                    sb.AppendFormat(" (cooling: {0})", Units.TicksToTime(sch.CooldownUntil - G.Ticks));
                }
            }
            if (ctx.Spacing != null) {
                var quietLeft = ctx.Spacing.QuietUntil(ctx) - ctx.Ticks;
                sb.AppendFormat("\n  Quiet after the last event: {0}",
                    quietLeft > 0 ? Units.TicksToTime(quietLeft) : "no");
            }

            // --- Raids ---
            sb.Append("\n\n<b>Raids:</b>");
            if (ctx.FlagCooldowns.TryGetValue(StoryEventFlags.Raid, out var raidTick)) {
                var raidIn = raidTick - ctx.Ticks;
                sb.AppendFormat("\n  Next raid in: {0}",
                    raidIn > 0 ? Units.TicksToTime(raidIn) : "due now");
            } else {
                sb.Append("\n  Next raid: no cooldown set");
            }
            sb.AppendFormat("\n  Raids survived: {0}", ctx.S.Situation.NumRaids);
            sb.Append("\n  Run 'story raid_report' for a full raid strength breakdown.");

            // --- Stasis wakeup ---
            sb.Append("\n\n<b>Stasis Wakeup:</b>");
            var gen = The.R.Query.GetStoryGen.Ask();
            bool foundWakeup = false;
            foreach (var asp in gen.StoryAspects) {
                if (asp is StasisWakeUpStoryAspect wake) {
                    var sit = ctx.S.Situation;
                    var bioIntent = ColonyGrowth.BioIntent(ctx);
                    sb.AppendFormat("\n  Population: {0:0.#} ({1} colonists) -> intent {2:0.00}, bio intent {3:0.00}",
                        sit.Population, sit.Colonists, ColonyGrowth.Intent(ctx), bioIntent);
                    if (bioIntent <= 0f) {
                        sb.Append("\n  Next wakeup: never (population at ceiling)");
                    } else {
                        var wakeIn = wake.NextWakeupTick(ctx) - ctx.Ticks;
                        sb.AppendFormat("\n  Next wakeup in: {0}",
                            wakeIn > 0 ? Units.TicksToTime(wakeIn) : "due now");
                    }
                    var holdLeft = ColonyGrowth.HoldUntilTick(ctx) - ctx.Ticks;
                    if (holdLeft > 0) {
                        sb.AppendFormat("\n  Growth hold: {0}", Units.TicksToTime(holdLeft));
                    }
                    if (ctx.FlagCooldowns.TryGetValue(StoryEventFlags.ColonyGrowthBio, out var joinerTick)
                            && joinerTick > ctx.Ticks) {
                        sb.AppendFormat("\n  Bio joiner cooldown: {0}",
                            Units.TicksToTime(joinerTick - ctx.Ticks));
                    }
                    foundWakeup = true;
                    break;
                }
            }
            if (!foundWakeup) {
                sb.Append("\n  No stasis wakeup aspect found");
            }

            WPopup.Create(false, IconId.CInfo, "Story Report", "Current narrative state", sb.ToString())
                .Spawn(center: true);
            return Close();
        }

        // Shows where every schedule's point allowance comes from and which events it prices
        // out. The allowance is the one story input that reads BaseNetWorth, so the header
        // prints the widened total next to the liquid subtotal: that ratio is what a wealth
        // redefinition actually does to story pacing, and it was unmeasurable before this.
        protected ConsoleCommandResult PointsReport(ConsoleCommandArguments args) {
            var s = The.R.State;
            var ctx = The.R.Query.GetStoryContext.Ask();
            var gen = The.R.Query.GetStoryGen.Ask();
            var sb = Caches.StringBuilder;
            sb.Clear();

            var econ = s.Sys.Economy;
            econ.UpdateNetWorth();
            var total = s.Situation.BaseNetWorth;
            var liquid = econ.BaseNetWorthLiquid / 1_000_000d;
            sb.AppendFormat("Wealth: <b>{0:0.##}M</b> total, {1:0.##}M liquid (x{2:0.00})",
                total, liquid, liquid > 0d ? total / liquid : 0d);
            sb.AppendFormat("\nDay {0}, {1} colonists, {2} workers, {3:0} kW, ship class {4}",
                s.Situation.Days, s.Situation.Colonists, s.Situation.Workers,
                s.Situation.GridMaxOutput, s.Situation.ShipClassPoints);

            foreach (var asp in gen.StoryAspects) {
                if (asp is not PointAllowanceStoryAspect pa) { continue; }
                var sch = ctx.ScheduleFor(pa.Outcome);
                sb.AppendFormat("\n\n<b>{0}</b>: {1}/{2} pts in current cycle",
                    pa.Outcome, sch.PointAllowance, sch.MaxPointAllowance);
                var next = pa.GetPointAllowance(ctx, sb);
                sb.AppendFormat("\n = <b>{0}</b> at next refresh", (int) next);
                AppendPricedOut(sb, pa.Outcome, sch.MaxPointAllowance);
            }

            WPopup.Create(false, IconId.CInfo, "Story Points Report",
                "Point allowance breakdown per schedule", sb.ToString())
                .Spawn(center: true);
            return Close();
        }

        private static void AppendPricedOut(StringBuilder sb, StoryEventOutcome outcome,
                int maxAllowance) {
            bool any = false;
            foreach (var def in StoryEventDef.Ordered) {
                if (def.Outcome != outcome || def.Points <= maxAllowance) { continue; }
                if (!any) {
                    any = true;
                    sb.Append(Texts.Red("\n Priced out (cost above max allowance):"));
                }
                sb.AppendFormat("\n  - {0}: {1} pts", def.Id, def.Points);
                if (def.Require != null && def.Require.Days > 0) {
                    sb.AppendFormat(" (needs day {0})", def.Require.Days);
                }
            }
            if (!any) {
                sb.Append(Texts.Grey("\n Every event of this outcome is affordable"));
            }
        }

        protected ConsoleCommandResult RaidReport(ConsoleCommandArguments args) {
            var s = The.R.State;
            var ctx = The.R.Query.GetStoryContext.Ask();
            var gen = The.R.Query.GetStoryGen.Ask();

            // Locate RaidStoryAspect for skip/storm state.
            RaidStoryAspect raidAspect = null;
            foreach (var asp in gen.StoryAspects) {
                if (asp is RaidStoryAspect r) { raidAspect = r; break; }
            }

            // Optional event id: preview that event's raid with its RaidStrengthMul folded in,
            // instead of the default MapEdge raid, so multi-entry raids can be tuned without
            // firing them.
            StoryEventDef evtDef = null;
            var evtId = args.GetString(2);
            if (evtId != null) {
                evtDef = StoryEventDef.Find(evtId.ToLowerInvariant(), false);
                if (evtDef == null) {
                    return Error($"Could not find story event with id: {evtId}");
                }
            }

            var raid = new CombatRaid(s, KL.Randomness.Rng.UInt);
            // Mirror the MapEdge raid configuration (the most common periodic raid).
            raid.ForceFlyingOnly = evtDef == null;

            float pendingStorm = 1f;
            if (raidAspect != null) {
                pendingStorm = raidAspect.PendingStormMul(ctx);
            }
            var eventMul = RaidEventUtils.StrengthMulFor(evtDef);
            var totalMul = pendingStorm * eventMul;
            if (totalMul != 1f) {
                // Apply the pending storm and the event's own multiplier so the breakdown
                // reflects the real next raid.
                raid.AdjustStrength(totalMul);
            }

            // Both are refreshed hourly on tick; recompute so the popup shows the colony as it
            // is now, not as it was up to an hour ago.
            s.Sys.Economy.UpdateNetWorth();
            s.Query.ColonyCombatStrength.Ask();

            // Don't set PosIdx — ComputePlan doesn't need it.
            var plan = raid.ComputePlan();

            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append("<b>Raid Strength Breakdown</b>");
            if (evtDef == null) {
                sb.Append("\n(What a MapEdge raid would look like right now)");
            } else {
                sb.AppendFormat("\n(What <b>{0}</b> would look like right now, RaidStrengthMul {1:0.##})",
                    evtDef.Id, eventMul);
                DescribeRaidEntries(sb, evtDef);
            }
            plan.Describe(sb);

            // ── Calm-before-the-storm state ──
            if (raidAspect != null) {
                sb.Append("\n\n<b>Calm Before the Storm:</b>");
                var countdown = raidAspect.RaidsUntilSkip(ctx);
                var armed = raidAspect.NextSkipArmed(ctx);
                sb.AppendFormat("\n  Raids until next skip slot: {0}", countdown);
                if (countdown == 0) {
                    sb.Append(armed
                        ? "\n  Skip slot: <b>armed</b> — next scheduled raid will be suppressed"
                        : "\n  Skip slot: not armed — next raid fires normally");
                }
                if (pendingStorm > 1f) {
                    sb.AppendFormat("\n  <b>Storm pending:</b> next raid is {0:0.##}× stronger",
                        pendingStorm);
                } else {
                    sb.Append("\n  No storm pending");
                }
                if (raidAspect.StrengthJitter > 0f) {
                    sb.AppendFormat("\n  Scheduled raids roll ±{0:0}% strength jitter on top (not shown above)",
                        raidAspect.StrengthJitter * 100f);
                }
            }

            WPopup.Create(false, IconId.CCombat, "Raid Report",
                "Current raid strength preview", sb.ToString())
                .Spawn(center: true);
            return Close();
        }

        // 'story adapt' shows raid adaptation; 'story adapt <days>' offsets it by that many
        // days (negative: as if crew had been lost)
        protected ConsoleCommandResult Adapt(ConsoleCommandArguments args) {
            var s = The.R.State;
            var ctx = The.R.Query.GetStoryContext.Ask();
            if (ctx == null) {
                return Error("No story context");
            }
            var before = RaidAdaptation.AdaptDays(ctx);
            if (args.GetString(2) != null) {
                RaidAdaptation.SetAdaptDays(ctx, before + args.GetFloat(2));
            }
            var now = RaidAdaptation.AdaptDays(ctx);
            return OK($"Raid adaptation: {now:0.#} days (was {before:0.#}), raid points x{RaidAdaptation.PointsMulAt(s, now):0.##}");
        }

        private static void DescribeRaidEntries(System.Text.StringBuilder sb, StoryEventDef def) {
            var minInst = def.Vars.Get(Game.CodeGen.PropertyIdH.MinInstances, 0);
            var maxInst = def.Vars.Get(Game.CodeGen.PropertyIdH.MaxInstances, 0);
            var maxCaps = def.Vars.Get(Game.CodeGen.PropertyIdH.MaxCapsules, 0);
            if (maxInst > 0) {
                sb.AppendFormat("\n  Entry objects: {0}-{1}, sharing the one budget below, up to {2} raiders each (at most {3} raiders)",
                    minInst, maxInst, Game.Components.HullpiercerComp.MaxRaidersPerPiercer,
                    maxInst * Game.Components.HullpiercerComp.MaxRaidersPerPiercer);
            } else if (maxCaps > 0) {
                sb.AppendFormat("\n  Entry objects: one raider per capsule, at most {0} raiders", maxCaps);
            }
            sb.Append("\n  Planned strength is an upper bound: raiders inside an object destroyed "
                + "before it opens are lost.");
        }

        protected ConsoleCommandResult RaidTest(ConsoleCommandArguments args) {
            var ctx = The.R.Query.GetStoryContext.Ask();
            var gen = The.R.Query.GetStoryGen.Ask();
            RaidStoryAspect raidAspect = null;
            foreach (var asp in gen.StoryAspects) {
                if (asp is RaidStoryAspect r) {
                    raidAspect = r;
                    break;
                }
            }
            if (raidAspect == null) {
                return Error("No RaidStoryAspect found in current story generator.");
            }
            // Optional event id, so one raid def (e.g. Raid/StasisPodRaid) can be exercised
            // without waiting for the picker to roll it.
            StoryEventDef only = null;
            var evtId = args.GetString(2);
            if (evtId != null) {
                only = StoryEventDef.Find(evtId.ToLowerInvariant(), false);
                if (only == null) {
                    return Error($"Could not find story event with id: {evtId}");
                }
            }
            The.R.State.IsAchievementsBlocked = true;
            var started = raidAspect.TriggerRaidNow(ctx, only: only);
            if (started == null) {
                // The trigger ignores the Raid cooldown and the point budget, but not each
                // event's own repeat delay and conditions, so it can start nothing at all
                return Error(only == null
                    ? "No raid event could start: each is held back by its repeat delay or its "
                        + "own conditions. Name one to force it past the picker, e.g. "
                        + "story raid_test Raid/MapEdgeRaid"
                    : $"{only.Id} could not start: its own conditions failed (see the log).");
            }
            var res = OK($"Triggered test raid {started.Def?.Id} (ignoring cooldown).");
            res.CloseConsole = true;
            return res;
        }

        protected ConsoleCommandResult CooldownList(ConsoleCommandArguments args) {
            var ctx = The.R.Query.GetStoryContext.Ask();
            var list = GetActiveCooldownList(ctx);
            return OK(list);
        }

        private static string GetActiveCooldownList(StoryContext ctx) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append("Active Cooldowns: ");
            foreach (var flag in StoryEventDef.CoolableFlags) {
                if (ctx.IsCoolingDown(flag)) {
                    sb.AppendFormat("\n - {0} -> {1}", flag,
                        Units.TicksToTime(ctx.FlagCooldowns[flag] - ctx.Ticks));
                }
            }
            return sb.ToString();
        }

        protected ConsoleCommandResult CooldownClear(ConsoleCommandArguments args) {
            var id = args.GetString(2);
            var ctx = The.R.Query.GetStoryContext.Ask();
            if (id == null) {
                return OK($"Usage: 'story cooldown_clear <flag>'\n{GetActiveCooldownList(ctx)}");
            }
            if (id.ToLower() == id) {
                id = char.ToUpper(id[0]) + id.Substring(1);
            }
            if (!Enum.TryParse<StoryEventFlags>(id, out var flag)) {
                return Error("Unknown cooldown flag: {0}. Options: {1}", id,
                    string.Join(", ", StoryEventDef.CoolableFlags));
            }
            ctx.CoolDownFlag(flag, 0);
            return OK("Cleared cooldown for flag: {0}", flag);
        }
        protected ConsoleCommandResult Schedule(ConsoleCommandArguments args) {
            var id = args.GetString(2);
            var ctx = The.R.Query.GetStoryContext.Ask();
            if (id == null) {
                return OK("Usage: 'schedule <neutral|positive|negative>' to start scheduling events of that outcome. Current schedules: {0}",
                    D.DumpCollection(ctx.Schedule));
            }
            if (id.ToLower() == id) {
                id = char.ToUpper(id[0]) + id.Substring(1);
            }
            if (!Enum.TryParse<StoryEventOutcome>(id, out var outcome)) {
                return Error("Unknown event schedule: {0}. Options: {1}", id,
                    string.Join(", ", Enum.GetValues(typeof(StoryEventOutcome))));
            }
            var schedule = ctx.Schedule[(int) outcome];
            var origSchedule = schedule;
            schedule.ChangeAfter = 0L;
            ctx.Schedule[(int) outcome] = schedule;
            return OK("Toggling schedule for outcome: {0} -> {1}", outcome, origSchedule);
        }
        protected ConsoleCommandResult Find(ConsoleCommandArguments args) {
            var res = "Matching events:";
            var id = args.GetString(2);
            if (id == null) {
                return List(args);
            }
            id = id.ToLowerInvariant();
            res += string.Join("\n - ", StoryEventDef.Ordered.Where(
                n => n.Id.ToLowerInvariant().Contains(id))
                    .Select(v => v.Id));
            if (res == "Matching events:") {
                return Error("Nothing found. See 'list' command for all ids");
            }
            return OK(res);
        }
        protected ConsoleCommandResult Disabled(ConsoleCommandArguments args) {
            var res = "Disabled events: ";
            int count = 0;
            foreach (var def in StoryEventDef.Ordered) {
                var isOff = ModifierDisableStoryEvents.IsDisabledFor(The.R.State, def);
                var state = isOff ? "disabled" : "enabled";
                if (isOff) {
                    res += $"\n - {def.Id}";
                    count++;
                }
            }
            if (count == 0) {
                res += "None";
            } else {
                res += $"\nTotal: {count}";
            }

            return OK(res);
        }
        protected ConsoleCommandResult Available(ConsoleCommandArguments args) {
            var res = "Available events:";
            var ctx = The.R.Query.GetStoryContext.Ask();
            int count = 0;
            foreach (var outcome in StoryEventDef.AllOutcomes) {
                var outcomeIdx = (int) outcome;
                var events = StoryEventDef.Considerable[outcomeIdx];
                if (Arrays.IsEmpty(events)) { continue; }
                var handler = ctx.S.Query.StoryEventHandlerFor.Ask(outcome);
                if (handler == null) { continue; }
                bool hasOutcome = false;
                foreach (var def in events) {
                    var weight = handler.GetEventWeight(ctx, def, StoryPickIgnore.None);
                    if (weight <= 0f) { continue; }
                    if (!hasOutcome) {
                        res += $"\n{outcome}:";
                        hasOutcome = true;
                    }
                    res += $"\n - {def.Id} ({weight:0.##})";
                    count++;
                }
            }
            if (count == 0) {
                res += " None";
            } else {
                res += $"\nTotal: {count}";
            }
            return OK(res);
        }
        protected ConsoleCommandResult Ongoing(ConsoleCommandArguments args) {
            var res = "Ongoing events:";
            var ctx = The.R.Query.GetStoryContext.Ask();

            foreach (var data in ctx.OngoingEvents) {
                res += $"\n - {data}";
            }
            return OK(res);
        }
        protected ConsoleCommandResult EndOngoing(ConsoleCommandArguments args) {
            var ctx = The.R.Query.GetStoryContext.Ask();
            int count = 0;
            foreach (var data in ctx.OngoingEvents) {
                if (data.IsEnded || data.State == StoryEventState.Ending) { continue; }
                data.SetState(ctx.S, StoryEventState.Ending);
                count++;
            }
            if (count == 0) {
                return OK("No ongoing story events to end");
            }
            return OK("Ending {0} ongoing story events", count);
        }
        protected ConsoleCommandResult List(ConsoleCommandArguments args) {
            var res = "All events:";
            foreach (var def in StoryEventDef.Ordered) {
                var isOff = ModifierDisableStoryEvents.IsDisabledFor(The.R.State, def);
                if (isOff) {
                    var text = Texts.Red("disabled");
                    res += $"\n - {Texts.Strike(def.Id)} ({text})";
                } else {
                    res += $"\n - {def.Id}";
                }
            }
            return OK(res);
        }


        private ConsoleCommandResult Toggle(ConsoleCommandArguments args, bool on) {
            var id = args.GetString(2);
            if (id == null) {
                return Error("Please provide event ID. Use 'list' or 'find' commands to find what the ids are.");
            }
            id = id.ToLowerInvariant();

            StoryEventDef ev = null;
            foreach (var def in StoryEventDef.Ordered) {
                if (def.Id.ToLowerInvariant() == id) {
                    ev = def;
                    break;
                }
            }
            if (ev == null) {
                return Error($"Could not find story event with id: {id}");
            }
            The.R.State.IsAchievementsBlocked = true;

            ModifierDisableStoryEvents.Get.SetDisabled(ev, !on, inGame: true);
            var state = on ? "enabled" : "disabled";
            return OK($"Story event {state}: {ev.Id}");
        }

        private ConsoleCommandResult Enable(ConsoleCommandArguments args) {
            return Toggle(args, true);
        }

        private ConsoleCommandResult Disable(ConsoleCommandArguments args) {
            return Toggle(args, false);
        }
        protected ConsoleCommandResult Check(ConsoleCommandArguments args) {
            var id = args.GetString(2);
            if (id == null) {
                return Error("Please provide event ID. Use 'list' or 'find' commands to find what the ids are.");
            }
            id = id.ToLowerInvariant();


            StoryEventDef ev = null;
            foreach (var evt in StoryEventDef.Ordered) {
                if (evt.Id.ToLowerInvariant().Contains(id)) {
                    ev = evt;
                    break;
                }
            }
            if (ev == null) {
                return Error($"Could not find story event with id: {id}");
            }

            var isOff = ModifierDisableStoryEvents.IsDisabledFor(The.R.State, ev);
            var state = isOff ? "disabled" : "enabled";
            var matches = The.R.State.Situation.Matches(ev.Require, The.R.State.Difficulty.Level);
            var config = ev.Tooltip;//D.DumpInstance(ev, condense: true, forLog: true);
            if (ev.Require != null) {
                return OK($"{ev.Id} is {state}. Story event matches current conditions: <b>{matches}</b>\nConfig: {config}\n<b>Current:</b> {The.R.State.Situation.MatchesDump(false, The.R.State.Difficulty.Level)}");
            } else {
                return OK($"{ev.Id} is {state}. Story event matches current conditions: <b>{matches}</b>\nConfig: {config}\nNo Requirements");
            }
        }

        protected ConsoleCommandResult Trigger(ConsoleCommandArguments args) {
            var id = args.GetString(2);
            if (id == null) {
                return Error("Please provide event ID. Use 'list' or 'find' commands to find what the ids are.");
            }
            id = id.ToLowerInvariant();

            if (id == "random") {
                var data = A.S.Query.TriggerStoryEvent.Ask(null);
                return OK("Triggered random event: {0}", data);
            }

            var ev = StoryEventDef.Find(id, false);
            if (ev == null) {
                return Error($"Could not find story event with id: {id}");
            }

            DevMenuTab_StoryEvents.CreateEventFrom(The.R.State, ev);

            The.R.State.IsAchievementsBlocked = true;
            var res = OK($"Enqueueing event {id} now");
            res.CloseConsole = true;
            return res;
        }


        public override void Initialize() {
            Name = "story";
            HelpLine = "Manages story generator events";
        }
    }
}
#endif
