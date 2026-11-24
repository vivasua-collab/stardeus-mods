using Game.Constants;
using Game.CodeGen;
using Game.Data;
using Game.Systems.Story;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectMagneticStorm : BaseStormSpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectMagneticStorm());
        }

        public const string EffectId = "MagneticStorm";
        public static readonly int EffectIdH = Hashes.S(EffectId);
        public override string Id => EffectId;
        public override string Name => T.EffectMagneticStorm;
        protected override string FanfareTitle => Texts.Yellow(Name);
        protected override string Desc => T.EffectMagneticStormDesc;
        protected override string Icon => IconId.CMagnet;
        private StoryEventDef _eventDef;
        protected override StoryEventDef eventDef => _eventDef ??= StoryEventDef.Get("Space/MagneticStorm");

        // True while a magnetic storm rages in the player's current sector (story-event
        // storm or permanent biome effect - both drive the same BB keys). AffectedId
        // legitimately rolls 0, the mod is always non-zero during a storm.
        public static bool IsOngoing(GameState s) {
            return s.BB.Get(BBKeyH.MagneticStormAffectedMod, 0) != 0;
        }

        public static bool SectorHasEffect(SpaceSector sector) {
            return sector.Biome.HasEffect(EffectIdH);
        }
    }
}
