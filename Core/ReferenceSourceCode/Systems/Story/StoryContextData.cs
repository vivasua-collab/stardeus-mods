using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using MonoMod.Utils;

namespace Game.Systems.Story {
    [MessagePackObject]
    public sealed class StoryContextData {
        [Key("OngoingEvents")] public List<StoryEventDataData> OngoingEvents;
        [Key("PastEvents")] public List<StoryEventDataData> PastEvents;
        [Key("Schedule")] public StorySchedule[] Schedule;
        [Key("RngState")] public uint RngState;
        [Key("Seen")] public List<EventSeenData> Seen;
        [Key("FlagCooldowns")] public Dictionary<StoryEventFlags, long> FlagCooldowns;
        [Key("PrioritizedEvents")] public Dictionary<int, long> PrioritizedEvents;
        [Key("AspectData")] public ComponentData AspectData;
        public static StoryContextData Serialize(StoryContext ctx)  {
            if (ctx == null) { return null; }
            var dd = new StoryContextData();
            dd.OngoingEvents = StoryEventDataData.Serialize(ctx.OngoingEvents);
            dd.PastEvents = StoryEventDataData.Serialize(ctx.PastEvents);
            dd.Seen = ctx.Seen.Values.ToList();
            dd.Schedule = ctx.Schedule;
            dd.PrioritizedEvents = ctx.PrioritizedEvents;
            dd.RngState = ctx.Rng.State;
            dd.FlagCooldowns = ctx.FlagCooldowns;
            dd.AspectData = ctx.AspectData;
            return dd;
        }

        public static StoryContext Deserialize(GameState s, StoryContextData data) {
            var c = new StoryContext(s);
            if (data != null) {
                StoryEventDataData.Deserialize(s, data.OngoingEvents, c.OngoingEvents);
                StoryEventDataData.Deserialize(s, data.PastEvents, c.PastEvents);
                c.Schedule = data.Schedule;
                c.Rng = Rng.Load(data.RngState);
                c.PrioritizedEvents.Clear();
                if (data.AspectData != null) {
                    c.AspectData = data.AspectData;
                }
                if (data.PrioritizedEvents != null) {
                    foreach (var ev in data.PrioritizedEvents) {
                        if (StoryEventDef.FastGet(ev.Key) == null) {
                            D.Err("Couldn't find prioritized story event by idH: {0}", ev);
                        } else {
                            c.PrioritizedEvents.Add(ev.Key, ev.Value);
                        }
                    }
                }

                if (!Arrays.IsEmpty(data.FlagCooldowns)) {
                    foreach (var cd in data.FlagCooldowns) {
                        if (!StoryEventDef.CoolableFlags.Contains(cd.Key)) {
                            D.Err("Skipping non-coolable flag in story context cooldowns: {0}", cd.Key);
                            continue;
                        }
                        c.FlagCooldowns.Add(cd.Key, cd.Value);
                    }
                }
                if (data.Seen != null) {
                    foreach (var seen in data.Seen) {
                        if (seen.Id == 0) { continue; }
                        c.Seen[seen.Id] = seen;
                    }
                }
                data.Schedule = null;
            }
            return c;
        }
    }
}
