using System.Collections.Generic;

namespace Game.Systems.Story {
    public interface IStoryEventHandler {
        StoryEventOutcome HandlesOutcome { get; }
        float GetEventWeight(StoryContext ctx, StoryEventDef def,
            StoryPickIgnore ignore);
        StoryEventData TriggerEvent(StoryEventDef def, StoryContext ctx);
        bool StartEvent(StoryEventData data, StoryContext ctx);
        bool CanStartNow(EventSeenData seen, StoryEventDef def,
            StoryContext ctx, StoryPickIgnore ignore);
        /// <summary>
        /// Will try to create and start the StoryEventData for one of the potential events.
        /// If no event was found, will return null.
        /// </summary>
        /// <param name="potential">preloaded list of potential weighted events</param>
        /// <param name="timeBudget">0f = infinite</param>
        /// <returns></returns>
        public StoryEventData TryStartPotential(
            StoryContext ctx, Dictionary<StoryEventDef, float> potential,
            float timeBudget);

        /// <summary>
        /// Will try to create the StoryEventData for one of the potential events.
        /// If no event was found, will return null.
        /// </summary>
        /// <param name="potential">preloaded list of potential weighted events</param>
        /// <param name="timeBudget">0f = infinite</param>
        /// <returns></returns>
        public StoryEventData TryPreparePotential(
            StoryContext ctx, Dictionary<StoryEventDef, float> potential,
            float timeBudget);
    }
}
