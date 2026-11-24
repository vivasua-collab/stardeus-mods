using Game.Constants;
using Game.Systems.Story;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectCometShower : BaseStormSpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectCometShower());
        }

        public const string EffectId = "CometShower";
        public override string Id => EffectId;
        public override string Name => T.EvCometShower;
        protected override string FanfareTitle => Texts.Green(Name);
        protected override string Desc => T.EvCometShowerDesc;
        protected override string Icon => IconId.CSparkles;
        private StoryEventDef _eventDef;
        protected override StoryEventDef eventDef => _eventDef ??= StoryEventDef.Get("Space/CometShower");
    }
}
