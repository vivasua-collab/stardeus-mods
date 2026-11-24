using Game.Data;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Visuals;
using KL.Utils;
using Game.Rendering;
using Game.Systems.Space;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class PhotonStormStoryEvent : BaseSpaceStormStoryEvent {
        public const string EventId = "PhotonStorm";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new PhotonStormStoryEvent());
        }

        public override string Icon => IconId.CEnergy;
        public override string Title => T.EvPhotonStorm;
        public override string Desc => T.EvPhotonStormDesc;
        public override NotificationType NotificationType => NotificationType.Info;
        public override bool OnCheckConditions(StoryContext ctx) {
            // prevent this to be layer on top of existing sector effect
            foreach (var eff in ctx.CurrentSector.Biome.Effects) {
                if (eff.Key.Id == SpaceEffectPhotonStorm.EffectId) {
                    return false;
                }
            }
            return true;
        }
        public override void ApplyEffect(GameState s, StoryEventDef def) {
            s.BB.Set(BBKeyH.SolarOutputMultiplier,
                Tunables.PhotonStormSolarOutputMultiplier);
        }
        public override void RemoveEffect(GameState s, StoryEventDef def) {
            s.BB.Set(BBKeyH.SolarOutputMultiplier, 1f);
        }
    }
}
