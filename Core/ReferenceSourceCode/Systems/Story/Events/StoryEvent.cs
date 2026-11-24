using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    public abstract class StoryEvent : IResettable {
        protected static void Register(StoryEvent e) {
            e.IdH = Hashes.S(e.Id);
            Registries.StoryEvents.Add(e.Id, e);
        }

        public abstract string Id { get; }
        public int IdH;
        // CANNOT HAVE A DEF, there can be more than one Def linked to this event!
        // For debugging / troubleshooting
        public readonly List<StoryEventDef> LinkedDefs = new();

        protected static bool HasForceAssignedGoal(BeingData being) {
            return being?.Being?.AIAgent.Goal?.IsForceAssigned == true;
        }

        public virtual bool CheckPoints(StoryContext ctx, StoryPickIgnore ignore,
                StoryEventDef def) {
            var pts = def.Points;
            if (pts == 0) { return true; }
            var schedule = ctx.Schedule[(int) def.Outcome];
            int allowance;
            // On demand events can happen out of schedule, so we'll use max allowance
            // to determine if event is available
            if ((ignore & StoryPickIgnore.Schedule) == StoryPickIgnore.Schedule ||
                    (def.Flags & StoryEventFlags.OnDemand) == StoryEventFlags.OnDemand) {
                allowance = schedule.MaxPointAllowance;
            } else {
                allowance = schedule.PointAllowance;
            }
            return allowance >= def.Points;
        }
        /// <summary>
        /// This has to:
        /// - set data.PointsCostActual
        /// - set data.FlagsActual,
        /// </summary>
        public virtual void OnFinalize(StoryContext ctx, StoryEventData data) {
            if (data.State == StoryEventState.Ended) {
                var points = data.Def.Points;
                data.PointsCostActual = points;
                data.EventFlagsActual = data.Def.Flags;
            }
        }
        /// <summary>
        /// If story event has to match some conditions, override this and
        /// implement the check.
        /// </summary>
        public virtual bool CheckConditions(StoryContext ctx, StoryEventDef def) { return true; }
        /// <summary>
        /// When a story event hook is being checked, this can filter out the
        /// events that cannot be executed for the hook.
        /// </summary>
        public virtual bool CheckHook(StoryContext ctx, StoryEventDef def, StoryEventHook hook) { return true; }
        public virtual void OnStart(StoryContext ctx, StoryEventData data) { }
        public virtual void OnLoad(StoryContext ctx, StoryEventData data) { }
        /// <summary>
        /// Warning. This will only get executed for events with state Ongoing.
        /// WaitingForHook will not trigger this!
        /// </summary>
        public virtual void OnTick(StoryContext ctx, StoryEventData data) {
            D.Err("OnTick is not overridden for Ongoing story event that did not end OnStart: {0}. Either override OnTick or set data.Status to Ending/Failed!",
                data);
            data.SetState(ctx.S, StoryEventState.Ending);
        }
        protected virtual void OnEnd(StoryContext ctx, StoryEventData data) { }
        public void End(StoryContext ctx, StoryEventData data) {
            data.EndTick = ctx.Ticks;
            // A throwing OnEnd would leave the event in Ending for good: EventEnd retries it
            // every tick and the exception stops the whole story generator (STA-1677)
            try {
                OnEnd(ctx, data);
            } catch (Exception ex) {
                D.LogEx(ex, string.Format("Story event failed to end cleanly: {0}", data));
            }
            data.SetState(ctx.S, StoryEventState.Ended);
        }
        public void Finalize(StoryContext ctx, StoryEventData data) {
            if (data.State == StoryEventState.Ending) {
                End(ctx, data);
            }
            OnFinalize(ctx, data);
            var outcome = data.Def.Outcome;
            if (data.State == StoryEventState.Ended) {
                ctx.LogSeen(data);
                if (!data.Def.Flags.HasFlag(StoryEventFlags.CooldownOnStart)) {
                    data.Flags |= StoryEventDataFlags.NeedsCooldown;
                }
            } else if (data.State == StoryEventState.Failed) {
                if (!data.LogEntry.Hide) {
                    if (data.LogEntry.IsValid) {
                        D.Err("Failed story event has a valid log entry! {0} -> {1}: {2}",
                            data, data.LogEntry.Title, data.LogEntry.Desc);
                    } else {
                        data.HideLogEntry();
                    }
                }
                if (data.StartTick > 0) {
                    var runTicks = ctx.Ticks - data.StartTick;
                    // event was running for a while (but not too long), we may want to remove the
                    // schedule cooldown
                    if (runTicks < Consts.TicksPerHour) {
                        var sched = ctx.ScheduleFor(data);
                        D.Warn("Removing schedule cooldown due to event failure: {0} -> {1}", sched, data);
                        sched.CooldownUntil = 0;
                    }
                }
                D.Warn("Event failed to run: {0}", data);
                // data.SetState(ctx.S, StoryEventState.Ended);
            } else {
                D.Err("Event ended but didn't set state to Ended / Failed! Setting to Ended: {0}",
                    data);
                data.SetState(ctx.S, StoryEventState.Ended);
            }
            PlayStats.TrackStoryEventEnd(data.Def.Id, data.State == StoryEventState.Ended);
            // Refund exactly what was reserved, minus what the event actually cost.
            // Using PointsReserved (the clamped real deduction) rather than Def.Points
            // prevents point creation when a reservation was partially absorbed by the clamp.
            var pointsDiff = data.PointsReserved - data.PointsCostActual;
            if (pointsDiff != 0) {
                ctx.ChangePoints(outcome, pointsDiff, data.Def, "Finalize points diff");
            }
        }

        public void OnReset() {
            Cleanup();
        }

        /// <summary>
        /// Override to remove any state that the story event may have.
        /// Ideally it should have none, but if you store references to
        /// particle systems or something like that, unset those references
        /// using this method.
        /// </summary>
        protected virtual void Cleanup() {
        }
        protected void PauseClock() {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                return;
            }
            #endif
            The.R?.State?.Clock.Pause();
        }
    }
}
