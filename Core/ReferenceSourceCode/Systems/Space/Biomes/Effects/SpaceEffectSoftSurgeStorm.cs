using Game.Constants;
using Game.Systems.Story;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectSoftSurgeStorm : BaseStormSpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectSoftSurgeStorm());
        }

        public override string Id => EffectId;
        public const string EffectId = "SoftSurgeStorm";
        public override string Name => T.EvSoftSurgeStorm;
        protected override string FanfareTitle => Texts.Green(T.EvSoftSurgeStorm);
        protected override string Desc => T.EvSoftSurgeStormDesc;
        protected override string Icon => IconId.CEnergy;
        private StoryEventDef _eventDef;
        protected override StoryEventDef eventDef => _eventDef ??= StoryEventDef.Get("Space/SoftSurgeStorm");
    }
}
