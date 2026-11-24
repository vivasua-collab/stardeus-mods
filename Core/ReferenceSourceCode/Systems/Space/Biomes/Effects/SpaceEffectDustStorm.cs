using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;
using Game.Visuals;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectDustStorm : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectDustStorm());
        }
        public const string EffectId = "DustStorm";
        public static readonly int EffectIdH = Hashes.S(EffectId);
        public override string Id => EffectId;
        public override string Name => T.EvtDustStormTitle;
        protected override string StoryEventId => "Space/DustStorm";
        private NotificationHandle notification;
        private VFXMapOverlay particles;
        private bool isRunning;
        protected override void Cleanup() {
            notification = default;
            particles = null;
            isRunning = false;
        }
        private void NotifyDustStorm(GameState s) {
            notification = Notification.Build(IconId.CWarning,
                    AddSectorPrefix(T.EvtDustStormTitle), 0, NotificationType.Warning)
                .Sticky()
                .WithTooltip(T.EvtDustStormDesc)
                .Group("space_storm", T.NounSpaceStorm, IconId.CSparkles)
                .Show();

            var title = AddSectorPrefix(Texts.Yellow(T.EvtDustStormTitle));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            if (isRunning || IsStoryEventDisabled(s)) { return; }
            isRunning = true;
            NotifyDustStorm(s);
            if (A.IsCosmeticParticlesOn) {
                particles = new VFXMapOverlay("DustStorm", s.Grid.Bounds);
                s.Sys.Particles.AddCustomParticles(particles);
                particles.Show();
            }
            s.BB.Set(BBKeyH.DustMultiplier, Tunables.DustStormAccumulationRate);
            s.BB.Set(BBKeyH.SolarOutputMultiplier, Tunables.DustStormSolarOutputMultiplier);
            SpaceSky.SetStorm(EffectId, true);
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            if (!isRunning) { return; }
            isRunning = false;
            if (notification.IsValid) {
                Notification.Remove(ref notification);
            }
            s.BB.Set(BBKeyH.DustMultiplier, 1f);
            s.BB.Set(BBKeyH.SolarOutputMultiplier, 1f);
            SpaceSky.SetStorm(EffectId, false);
            if (particles != null) {
                s.Sys.Particles.RemoveCustomParticles(particles);
                particles.PhaseOut(10f);
                particles = null;
            }
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
            // A freshly loaded game starts with no storm running
            isRunning = false;
            OnEnterSector(s, sector);
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
            if (!IsSwitchCheckTick(ticks)) { return; }
            var isDisabled = IsStoryEventDisabled(s);
            if (isRunning && isDisabled) {
                OnExitSector(s, currentSector);
            } else if (!isRunning && !isDisabled) {
                OnEnterSector(s, currentSector);
            }
        }
    }
}
