using System;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class StoryEventHook {
        public StoryEventFlags Flags;
        // Event must be picked and started in the same frame
        public bool IsInstant;
        public StoryPickIgnore Ignore = StoryPickIgnore.None;
        public object Target;
        public int Seed;
        public int BoundToSO;
        public Action<StoryEventData> BeforeStart;
        public Action<StoryEventData> AfterStart;
        public Func<StoryEventDef, bool> EventFilter;
        /// <summary>
        /// Optional hooks do not trigger errors if they fail to find a suitable event
        /// </summary>
        public bool IsOptional;
        private StoryEventHook() { }
        public static StoryEventHook Create(StoryEventFlags flags) {
            var hook = new StoryEventHook();
            hook.Flags = flags;
            return hook;
        }
        public static StoryEventHook CreateFor(object target, StoryEventFlags flags) {
            var hook = new StoryEventHook();
            hook.Target = target;
            hook.Flags = flags;
            if (target == null) {
                D.Err("Creating a hook with null target! {0}", hook);
            }
            return hook;
        }
        public StoryEventHook WithSeed(int seed) {
            Seed = seed;
            return this;
        }
        public StoryEventHook WithIgnore(StoryPickIgnore ignore) {
            Ignore = ignore;
            return this;
        }

        public StoryEventHook WithBeforeStart(Action<StoryEventData> act) {
            BeforeStart = act;
            return this;
        }
        public StoryEventHook WithAfterStart(Action<StoryEventData> act) {
            AfterStart = act;
            return this;
        }
        public StoryEventHook WithEventFilter(Func<StoryEventDef, bool> filter) {
            EventFilter = filter;
            return this;
        }

        public StoryEventHook Optional() {
            IsOptional = true;
            return this;
        }
        public StoryEventHook Instant() {
            IsInstant = true;
            // for guaranteed fallback
            Ignore = StoryPickIgnore.All;
            return this;
        }

        public override string ToString() {
            return $"StoryEventHook[{Flags}|I:{IsInstant}|T:{Target}|BS:{BeforeStart?.Target}|AS:{AfterStart?.Target}]";
        }
    }
}
