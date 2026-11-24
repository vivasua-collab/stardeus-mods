#if !PRODUCTION_BUILD
// #define DEBUG_END
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class EventEndStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new EventEndStoryAspect();
        }

        public const string AspectId = "EventEnd";
        public override string Id => AspectId;

        public override void OnTick(StoryContext ctx) {
            for (int i = ctx.OngoingEvents.Count - 1; i >= 0; i--) {
                var data = ctx.OngoingEvents[i];
                if (data.State == StoryEventState.Ending) {
                    #if DEBUG_END
                    D.Warn("Ending Event: {0}", data);
                    #endif
                    FinalizeEvent(ctx, data);
                    if (data.State != StoryEventState.Ended) {
                        D.Err("Finalized story event did not transition to Ended: {0}", data);
                        continue;
                    }
                    ctx.OngoingEvents.RemoveAt(i);
                    ctx.PastEvents.Add(data);
                } else if (data.State == StoryEventState.Failed) {
                    #if DEBUG_END
                    D.Warn("Removing Failed Event: {0}", data);
                    #endif
                    FinalizeEvent(ctx, data);
                    ctx.OngoingEvents.RemoveAt(i);
                    // give points back
                    // ctx.ChangePoints(data.Def.Outcome, -data.Def.Points);
                } else {
                    continue;
                }
            }
        }
        private void FinalizeEvent(StoryContext ctx, StoryEventData data) {
            data.Def.Event.Finalize(ctx, data);
            if ((data.Flags & StoryEventDataFlags.NeedsCooldown)
                    == StoryEventDataFlags.NeedsCooldown) {
                ctx.CooldownEvent(data);
            }
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
        }
        public override void Unload(StoryContext ctx) {
        }
    }
}
