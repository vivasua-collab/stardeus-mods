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
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class CometShowerStoryEvent : BaseSpaceStormStoryEvent {
        public const string EventId = "CometShower";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new CometShowerStoryEvent());
        }

        public override bool CooldownStorms => false;

        public override bool OnCheckConditions(StoryContext ctx) {
            return true;
        }
        public override string Title => T.EvCometShower;
        public override string Desc => T.EvCometShowerDesc;
        public override string Icon => IconId.CSparkles;
        public override NotificationType NotificationType => NotificationType.Info;

        public override VFXHolder CreateParticles(GameState s, StoryEventDef def) {
            if (RenderUtils.DisableCosmeticVFX) { return null; }
            var particles = new VFXHolder(
                def.Vars.Get(PropertyIdH.Particles, "ParticleRain"),
                layerName: "StarfieldDynamic",
                hideInFullScreenWidget: true);
            if (!particles.IsSpawned) { return null; }
            particles.VFX.gameObject.transform.position = new Vector3(0f, 0f, 800f);
            particles.SetScale(400f);
            particles.SetPlayRate(1f);
            return particles;
        }

        public override int MoodSourceH => MoodSourceIdH.CometShower;
        public override int MoodEffect => 10;

        public override void ApplyEffect(GameState s, StoryEventDef def) {
        }
        public override void RemoveEffect(GameState s, StoryEventDef def) {
        }
    }
}
