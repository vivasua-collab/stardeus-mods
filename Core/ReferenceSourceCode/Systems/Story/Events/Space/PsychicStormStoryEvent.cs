using Game.Data;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Visuals;
using KL.Utils;
using Game.Rendering;
using Game.Systems.Space;
using Game.Systems.Mental;
using KL.Grid;

namespace Game.Systems.Story {
    public sealed class PsychicStormStoryEvent : BaseSpaceStormStoryEvent {
        public const string EventId = "PsychicStorm";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new PsychicStormStoryEvent());
        }
        public override string Icon => IconId.CTraits;
        public override string Title => T.EvPsychicStorm;
        public override string Desc => T.EvPsychicStormDesc;
        public override int MoodSourceH => MoodSourceIdH.PsychicStorm;
        // Every colonist aboard for 3-4 days: at -10 the whole crew sat at the medium
        // breakdown band on Balanced
        public override int MoodEffect => -5;
        public override bool OnCheckConditions(StoryContext ctx) {
            return true;
        }

        public override void ApplyEffect(GameState s, StoryEventDef def) {
        }

        public override void RemoveEffect(GameState s, StoryEventDef def) {
        }
    }
}
