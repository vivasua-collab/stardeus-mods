using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Space;
using Game.Systems.Notify;
using UnityEngine;
using UnityEngine.Rendering;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class SoftSurgeStormStoryEvent : BaseSpaceStormStoryEvent {
        public const string EventId = "SoftSurgeStorm";
        public override string Icon => IconId.CEnergy;
        public override string Title => T.EvSoftSurgeStorm;
        public override string Desc => T.EvSoftSurgeStormDesc;
        public override string Id => EventId;
        public override NotificationType NotificationType => NotificationType.Info;
        [SelfInit]
        private static void Register() {
            Register(new SoftSurgeStormStoryEvent());
        }
        public override bool OnCheckConditions(StoryContext ctx) {
            // prevent this to be layer on top of existing sector effect
            foreach (var eff in ctx.CurrentSector.Biome.Effects) {
                if (eff.Key.Id == SpaceEffectSoftSurgeStorm.EffectId) {
                    return false;
                }
            }
            return true;
        }

        public override void ApplyEffect(GameState s, StoryEventDef def) {
            s.BB.Set(BBKeyH.EnergyConsumptionMul, 0.5f);
        }

        public override void RemoveEffect(GameState s, StoryEventDef def) {
            s.BB.Set(BBKeyH.EnergyConsumptionMul, 1f);
        }
    }
}
