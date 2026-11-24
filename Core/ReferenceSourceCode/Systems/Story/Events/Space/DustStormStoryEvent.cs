using Game.Data;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Visuals;
using Game.Data.Space;
using KL.Utils;
using Game.Systems.Space;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class DustStormStoryEvent : StoryEvent {
        public const string EventId = "DustStorm";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DustStormStoryEvent());
        }
        private VFXMapOverlay particles;
        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            // prevent this to be layer on top of dust storm sector effect
            if (ctx.HasSectorEffect(SpaceEffectDustStorm.EffectIdH)) {
                return false;
            }
            if (ctx.IsEventRunning(MagneticStormStoryEvent.EventIdH, 1.5f)) {
                return false;
            }
            if (ctx.HasSectorEffect(SpaceEffectMagneticStorm.EffectIdH, 1.5f)) {
                return false;
            }
            return true;
        }
        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var vars = data.GetOrCreateData();
            var rng = data.SeededRng;
            var s = ctx.S;
            var duration = Mathf.RoundToInt(
                rng.Range(Consts.TicksPer16Hours, Consts.TicksPer3Days)
                    * s.Difficulty.Level);
            if (duration > Consts.TicksPer7Days) {
                duration = Consts.TicksPer7Days
                    - rng.Range(Consts.TicksPerHour, Consts.TicksPerDay);
            }
            vars.SetInt("Duration", duration);
            vars.SetInt("Sector", ctx.CurrentSector.Id);
            data.SetState(s, StoryEventState.Starting);
            data.CreateLogEntry(IconId.CWarning,
                T.EvtDustStormTitle, T.EvtDustStormDesc);
        }

        private void NotifyDustStorm(StoryContext ctx, StoryEventData data) {
            data.Event = Notification.Build(IconId.CWarning,
                    T.EvtDustStormTitle, 0, NotificationType.Warning)
                .Sticky()
                .WithOnClick(data.ShowPopup)
                .WithTooltip(T.EvtDustStormDesc)
                .Group("space_storm", T.NounSpaceStorm, IconId.CSparkles)
                .Show();
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.Data == null) {
                data.FailEvent(s, "Data is null, can't determine duration");
                return;
            }
            var duration = data.Data.GetInt("Duration", Consts.TicksPerDay);
            if (G.Ticks > data.StartTick + duration) {
                data.SetState(s, StoryEventState.Ending);
                return;
            }
            if (A.IsInHyperspace) {
                data.SetState(s, StoryEventState.Ending);
                return;
            }
            if (data.Data.GetInt("Sector", 0) != ctx.CurrentSector.Id) {
                D.Warn("Sector changed, killing dust storm");
                data.SetState(s, StoryEventState.Ending);
                return;
            }
            if (!data.Event.IsValid) {
                NotifyDustStorm(ctx, data);
                if (particles != null) {
                    D.Err("Particles not null when applying dust storm! {0}",
                        particles);
                }
                // D.Warn("Applying dust storm effect");
                if (A.IsCosmeticParticlesOn) {
                    particles = new VFXMapOverlay("DustStorm", s.Grid.Bounds);
                    s.Sys.Particles.AddCustomParticles(particles);
                    particles.Show();
                }
                s.BB.Set(BBKeyH.DustMultiplier,
                    Tunables.DustStormAccumulationRate);
                s.BB.Set(BBKeyH.SolarOutputMultiplier,
                    Tunables.DustStormSolarOutputMultiplier);
                SpaceSky.SetStorm(SpaceEffectDustStorm.EffectId, true);
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.RemoveNotification();
            s.BB.Set(BBKeyH.DustMultiplier, 1f);
            s.BB.Set(BBKeyH.SolarOutputMultiplier, 1f);
            SpaceSky.SetStorm(SpaceEffectDustStorm.EffectId, false);
            s.Sig.ShowFanfare.Send(Texts.Green(T.EvtDustStormEndTitle));
            var duration = data.Data.GetInt("Duration", Consts.TicksPerDay);
            s.Sys.Log.AddLine(T.EvtDustStormEndDesc_V1.F(
                Mathf.RoundToInt(duration / Consts.TicksPerHour)));
            if (particles != null) {
                D.Warn("Removing dust storm particles");
                s.Sys.Particles.RemoveCustomParticles(particles);
                particles.PhaseOut(10f);
                particles = null;
            }
        }

        protected override void Cleanup() {
            particles = null;
        }
    }
}
