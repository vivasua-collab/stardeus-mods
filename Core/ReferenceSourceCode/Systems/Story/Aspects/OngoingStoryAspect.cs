using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class OngoingStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new OngoingStoryAspect();
        }

        public const string AspectId = "Ongoing";
        public override string Id => AspectId;
        private int defaultMaxOngoingTicks;
        private int defaultMaxOngoingTicksHook;

        public override void OnTick(StoryContext ctx) {
            foreach (var data in ctx.OngoingEvents) {
                if (data.State == StoryEventState.Starting) {
                    if (data.Def.Flags.HasFlag(StoryEventFlags.CooldownOnStart)) {
                        ctx.CooldownEvent(data);
                    }
                    data.SetState(ctx.S, StoryEventState.Ongoing);
                } else if (data.State == StoryEventState.Ending) {
                    // handled in EndEventStoryAspect
                    continue;
                }
                var isOngoing = data.State == StoryEventState.Ongoing;
                if (isOngoing) {
                    data.Def.Event.OnTick(ctx, data);
                    // update ongoing after tick
                    isOngoing = data.State == StoryEventState.Ongoing;
                }
                var canTimeout = (isOngoing || data.State == StoryEventState.WaitingForHook)
                    // if there's never timeout flag, it can't timeout
                    && (data.Def.Flags & StoryEventFlags.NeverTimeout) == StoryEventFlags.None;
                if (canTimeout) {
                    var maxTicks = MaxTicksFor(data);
                    var minStartTick = ctx.Ticks - maxTicks;
                    if (data.StartTick < minStartTick) {
                        TimeoutEvent(ctx, data);
                    }
                }
            }
        }

        private static void TimeoutEvent(StoryContext ctx, StoryEventData data) {
            var runtime = Units.TicksToTime(ctx.Ticks - data.StartTick);
            if (data.State == StoryEventState.WaitingForHook) {
                D.Warn("Waiting for a hook for too long: {0}", data);
            } else {
                D.Err("Failing a story event that was running for {0}. If such a long run was intended, set 'MaxOngoingDays' in JSON for the event: {1}",
                    runtime, data);
            }
            data.FailEvent(ctx.S, $"Runtime exceeded limit: {runtime}",
                isHardFailure: false);
        }

        private int MaxTicksFor(StoryEventData data) {
            var maxTicks = data.Def.MaxOngoingTicks;
            if (maxTicks == 0) {
                maxTicks = DefaultMaxOngoingFor(data.State);
            }
            return maxTicks;
        }

        private int DefaultMaxOngoingFor(StoryEventState state) {
            switch (state) {
                case StoryEventState.Ongoing: return defaultMaxOngoingTicks;
                case StoryEventState.WaitingForHook: return defaultMaxOngoingTicksHook;
                default: {
                    D.Err("Unhandled state: {0}", state);
                    return defaultMaxOngoingTicks;
                }
            }
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            var maxOngoingDays = Def.Vars.Get(PropertyIdH.MaxOngoingEventDaysDefault, 3f);
            defaultMaxOngoingTicks = (int) (maxOngoingDays * Consts.TicksPerDay);
            var maxOngoingDaysHook = Def.Vars.Get(PropertyIdH.MaxOngoingEventDaysDefault_Hook, 21f);
            defaultMaxOngoingTicksHook = (int) (maxOngoingDaysHook * Consts.TicksPerDay);
        }

        public override void Unload(StoryContext ctx) {
        }
    }
}
