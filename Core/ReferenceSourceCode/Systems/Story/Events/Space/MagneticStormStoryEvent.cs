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
    public sealed class MagneticStormStoryEvent : BaseSpaceStormStoryEvent {
        public const string EventId = "MagneticStorm";
        public static readonly int EventIdH = Hashes.S(EventId);
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new MagneticStormStoryEvent());
        }

        public override string Icon => IconId.CMagnet;
        public override string Title => T.EffectMagneticStorm;
        public override string Desc => T.EvMagneticStormDesc;
        public override NotificationType NotificationType => NotificationType.Alert;
        private long nextEffectChangeAt;
        public override bool OnCheckConditions(StoryContext ctx) {
            // prevent this to be layer on top of existing sector effect
            if (ctx.HasSectorEffect(SpaceEffectMagneticStorm.EffectIdH)) {
                return false;
            }
            if (ctx.IsEventRunning(MicroMeteoroidStrikeStoryEvent.EventIdH, 1.5f)) {
                return false;
            }
            if (ctx.HasSectorEffect(SpaceEffectDustStorm.EffectIdH, 1.5f)) {
                return false;
            }
            return true;
        }
        public override void ApplyEffect(GameState s, StoryEventDef def) {
            nextEffectChangeAt = 0;
        }
        public override void RemoveEffect(GameState s, StoryEventDef def) {
            nextEffectChangeAt = 0;
            s.BB.Remove(BBKeyH.MagneticStormAffectedId, 0);
            s.BB.Remove(BBKeyH.MagneticStormAffectedMod, 0);
        }

        public override void TickStorm(GameState s, StoryEventDef def) {
            if (nextEffectChangeAt > s.Ticks) {
                return;
            }
            nextEffectChangeAt = s.Ticks + s.Rng.Range(
                Consts.TicksPer10Minutes, Consts.TicksPer60Minutes);
            int mod = 5;
            int affectedId = s.Rng.Range(0, mod);
            s.BB.Set(BBKeyH.MagneticStormAffectedId, affectedId);
            s.BB.Set(BBKeyH.MagneticStormAffectedMod, mod);
        }

        protected override void Cleanup() {
            nextEffectChangeAt = 0;
            base.Cleanup();
        }
    }
}
