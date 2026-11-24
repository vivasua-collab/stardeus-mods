using Game.Constants;
using Game.Systems.Story;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectPsychicStorm : BaseStormSpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectPsychicStorm());
        }

        public override string Id => EffectId;
        public const string EffectId = "PsychicStorm";
        public override string Name => T.EvPsychicStorm;
        protected override string FanfareTitle => Texts.Yellow(T.EvPsychicStorm);
        protected override string Desc => T.EvPsychicStormDesc;
        protected override string Icon => IconId.CTraits;
        private StoryEventDef _eventDef;
        protected override StoryEventDef eventDef => _eventDef ??= StoryEventDef.Get("Space/PsychicStorm");
    }
}
