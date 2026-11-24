using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using Game.Utils;
using Game.Visuals;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectPhotonStorm : BaseStormSpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectPhotonStorm());
        }

        public override string Id => EffectId;
        public const string EffectId = "PhotonStorm";
        public override string Name => T.EvPhotonStorm;
        protected override string FanfareTitle => Texts.Green(T.EvPhotonStorm);
        protected override string Desc => T.EvPhotonStormDesc;
        protected override string Icon => IconId.CEnergy;
        private StoryEventDef _eventDef;
        protected override StoryEventDef eventDef => _eventDef ??= StoryEventDef.Get("Space/PhotonStorm");
    }
}
