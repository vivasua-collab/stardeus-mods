using Game.Data;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Visuals;
using KL.Utils;
using Game.Rendering;
using UnityEngine.VFX;
using Game.Systems.Notify;
using Game.Systems.Creatures;

namespace Game.Systems.Story {
    public abstract class BaseSpaceStormStoryEvent : StoryEvent {
        public abstract void ApplyEffect(GameState s, StoryEventDef def);
        public abstract void RemoveEffect(GameState s, StoryEventDef def);
        public virtual void TickStorm(GameState s, StoryEventDef def) { }
        public abstract bool OnCheckConditions(StoryContext ctx);

        public abstract string Icon { get; }
        public abstract string Title { get; }
        public abstract string Desc { get; }
        public virtual bool CooldownStorms => true;
        public virtual NotificationType NotificationType => NotificationType.Warning;
        // MoodSourceIdH of the mood this storm puts on everyone aboard, 0 for none. It is a
        // permanent effect that lasts exactly as long as the storm.
        public virtual int MoodSourceH => 0;
        public virtual int MoodEffect => 0;

        // Shared by story storms and sector storm effects (BaseStormSpaceEffect), so the sky
        // shows every storm the same way however it started
        public void StartStormEffects(GameState s, StoryEventDef def) {
            ApplyEffect(s, def);
            SetStormMood(s);
            SpaceSky.SetStorm(Id, true);
        }

        public void TickStormEffects(GameState s, StoryEventDef def) {
            // Confirms the mood well within the permanent effect lapse, and gives it to
            // whoever came aboard since
            if (s.Ticks % Consts.TicksPerHour == 17) {
                SetStormMood(s);
            }
            TickStorm(s, def);
        }

        public void StopStormEffects(GameState s, StoryEventDef def) {
            RemoveEffect(s, def);
            ClearStormMood(s);
            SpaceSky.SetStorm(Id, false);
        }

        private void SetStormMood(GameState s) {
            if (MoodSourceH == 0) { return; }
            var alive = s.Beings.Alive;
            for (int i = alive.Count - 1; i >= 0; i--) {
                alive[i].Data.Mind.Health?.SetPermanentEffect(s, MoodSourceH, Icon, Title,
                    MoodEffect);
            }
        }

        // Every mind, not only the alive ones aboard: whoever died, went into stasis or left
        // during the storm must not carry it on
        private void ClearStormMood(GameState s) {
            if (MoodSourceH == 0) { return; }
            var ticks = G.Ticks;
            foreach (var mind in s.Sys.Get<BeingsSys>(BeingsSys.SysId).Minds.Values) {
                mind.Health?.RemovePermanentEffect(MoodSourceH, ticks);
            }
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (A.IsInHyperspace) { return false; }
            // prevent this to be layer on top of existing sector effect: both would own the
            // same mood and sky layer, and the first to end would take them from the other
            foreach (var eff in ctx.CurrentSector.Biome.Effects) {
                if (eff.Key.Id == Id) {
                    return false;
                }
            }
            // extra check for never mixing storms
            if (CooldownStorms) {
                foreach (var evt in ctx.OngoingEvents) {
                    if (evt.Def.Event is BaseSpaceStormStoryEvent) {
                        return false;
                    }
                }
            }
            return OnCheckConditions(ctx);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var vars = data.GetOrCreateData();
            var rng = data.SeededRng;
            var s = ctx.S;
            float durDaysMin = data.Def.Vars.Get(PropertyIdH.DurationDaysMin, 2f);
            float durDaysMax = data.Def.Vars.Get(PropertyIdH.DurationDaysMax, 2f);
            var duration = Mathf.RoundToInt(
                rng.Range(durDaysMin, durDaysMax) * Consts.TicksPerDay);
            vars.SetInt("Duration", duration);
            vars.SetInt("Sector", ctx.CurrentSector.Id);
            data.SetState(s, StoryEventState.Starting);
            data.CreateLogEntry(Icon, Title, Desc);
        }

        private void Notify(StoryContext ctx, StoryEventData data) {
            var notif = new NotificationData();
            notif.Type = NotificationType;
            notif.Icon = Icon;
            notif.Title = Title;
            notif.Tooltip = Desc;
            notif.OnClick = data.ShowPopup;
            notif.Flags |= NotificationFlags.AutoAction | NotificationFlags.Sticky;
            notif.GroupId = "space_storm";
            notif.GroupTitle = T.NounSpaceStorm;
            notif.GroupIcon = IconId.CSparkles;

            data.Event = ctx.S.Query.AddNotification.Ask(notif);
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.Data == null) {
                data.FailEvent(s, "Data is null, can't determine duration");
                return;
            }
            var duration = data.Data.GetInt("Duration", Consts.TicksPerDay);
            var endTime = data.StartTick + duration;
            if (G.Ticks > endTime) {
                // delay ending while camera is zoomed out, because
                // particles will be abruptly removed and it looks bad unless
                // we're close to ship surface and won't see it
                if (RenderUtils.CameraOrtho < 12
                        || G.Ticks > endTime + Consts.TicksPer1Days) {
                    data.SetState(s, StoryEventState.Ending);
                    return;
                }
            }
            if (CooldownStorms && ctx.Ticks % 15 == 2) {
                ctx.AutoCoolDownFlag(StoryEventFlags.SpaceStorm, Consts.TicksPer3Days);
            }
            if (A.IsInHyperspace) {
                data.SetState(s, StoryEventState.Ending);
                return;
            }
            if (data.Data.GetInt("Sector", 0) != ctx.CurrentSector.Id) {
                D.Warn("Sector changed, killing storm: {0}", data.Def.Id);
                data.SetState(s, StoryEventState.Ending);
                return;
            }

            TickStormEffects(ctx.S, data.Def);

            if (data.Event.IsValid) { return; }

            Notify(ctx, data);
            if (data.TempObj is VFXHolder particles) {
                D.Err("Particles not null when applying storm! {0} -> {1}",
                    data.Def.Id, particles);
                particles.Destroy();
                data.TempObj = null;
            }
            if (A.IsCosmeticParticlesOn) {
                particles = CreateParticles(s, data.Def);
                if (particles != null) {
                    s.Sys.Particles.AddCustomParticles(particles);
                    particles.Show();
                    data.TempObj = particles;
                }
            }
            StartStormEffects(ctx.S, data.Def);
        }

        public virtual VFXHolder CreateParticles(GameState s, StoryEventDef def) {
            if (!def.Vars.TryString(PropertyIdH.Particles, out var particleSys)) {
                D.Err("Particles variable not defined in story event def: {0}", def.Id);
                return null;
            }
            var particles = new VFXHolder(particleSys);
            if (!particles.IsSpawned) { return null; }
            Color color;
            if (!def.Vars.TryFloatSet(PropertyIdH.ParticleColor, out var colorSet)) {
                D.Err("ParticleColor variable not defined in story event def: {0}", def.Id);
                color = Consts.ColorWhite;
            } else {
                color = new Color(colorSet[0], colorSet[1], colorSet[2], colorSet[3]);
            }
            var turbIntensity = def.Vars.Get(PropertyIdH.ParticleTurbulenceIntensity, 1.5f);
            var turbDrag = def.Vars.Get(PropertyIdH.ParticleTurbulenceDrag, 1f);
            var turbFreq = def.Vars.Get(PropertyIdH.ParticleTurbulenceFrequency, 0.34f);
            var sphereAttrForce = def.Vars.Get(PropertyIdH.ParticleSphereAttractionForce, 20f);
            var sphereAttrStickDist = def.Vars.Get(PropertyIdH.ParticleSphereAttractionStickDist, 50f);
            var playRate = def.Vars.Get(PropertyIdH.ParticlePlayRate, 1f);
            var velocity = def.Vars.Get(PropertyIdH.ParticleVelocity, 0.01f);
            var partSize = def.Vars.Get(PropertyIdH.ParticleSize, 3f);

            particles.VFX.gameObject.transform.position = s.Grid.Center;
            var scale = s.Grid.Width * 0.33f;
            particles.SetScale(scale);
            particles.SetPlayRate(playRate);

            particles.VFX.SetFloat(ShaderVars.Size, partSize);
            particles.VFX.SetVector3(ShaderVars.Turbulence,
                new Vector3(turbIntensity, turbDrag, turbFreq));
            particles.VFX.SetVector2(ShaderVars.SphereAttraction,
                new Vector2(sphereAttrForce, sphereAttrStickDist));
            particles.VFX.SetVector4(ShaderVars.Color, color);
            particles.VFX.SetFloat(ShaderVars.Velocity, velocity);
            return particles;
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            StopStormEffects(s, data.Def);
            data.RemoveNotification();
            var line = Texts.Yellow(T.EventEnded_V1.F(Title));
            s.Sig.ShowFanfare.Send(line);
            s.Sys.Log.AddLine(line);
            if (data.TempObj is VFXHolder particles) {
                s.Sys.Particles.RemoveCustomParticles(particles);
                particles.Destroy();
                data.TempObj = null;
            }
        }
    }
}
