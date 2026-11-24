using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Modifiers;
using Game.Systems.Notify;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.Utils;

namespace Game.Systems.Space {
    public abstract class BaseStormSpaceEffect : SpaceEffect {
        private NotificationHandle notification;
        private VFXHolder particles;
        // False while the player has the storm's story event disabled (STA-1686): a
        // permanent storm is still that event, so it obeys the same switch
        private bool isRunning;
        protected abstract StoryEventDef eventDef { get; }
        protected abstract string Icon { get; }
        protected abstract string FanfareTitle { get; }
        protected abstract string Desc { get; }
        protected NotificationType NotificationType =>
            eventDef.Event is BaseSpaceStormStoryEvent storm
                ? storm.NotificationType : NotificationType.Warning;

        protected override void Cleanup() {
            notification = default;
            particles = null;
            isRunning = false;
        }

        private bool IsDisabled(GameState s) {
            return ModifierDisableStoryEvents.IsDisabledFor(s, eventDef);
        }
        private void Notify(GameState s) {
            var title = AddSectorPrefix(FanfareTitle);
            var tip = $"{title}{GlyphMap.BRHR}{Desc}";
            notification = Notification.Build(Icon, AddSectorPrefix(Name),
                    0, NotificationType)
                .Sticky()
                .WithTooltip(tip)
                .WithOnClick(() => {
                    The.Dialog.ShowSimple(Icon, tip);
                })
                .Group("space_storm", T.NounSpaceStorm, IconId.CSparkles)
                .Show();
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            if (eventDef.Event is not BaseSpaceStormStoryEvent storm) {
                D.Err("Misconfigured BaseStormSpaceEffect, def is not BaseSpaceStormStoryEvent: {0} -> {1}",
                    eventDef, eventDef.EventId);
                return;
            }
            if (isRunning || IsDisabled(s)) { return; }
            isRunning = true;
            Notify(s);

            // prevent overlapping storms of other types
            var ctx = s.Query.GetStoryContext.Ask();
            ctx.AutoCoolDownFlag(StoryEventFlags.SpaceStorm);

            storm.StartStormEffects(s, eventDef);
            if (!A.IsCosmeticParticlesOn) { return; }
            particles = storm.CreateParticles(s, eventDef);
            if (particles != null) {
                s.Sys.Particles.AddCustomParticles(particles);
                particles.Show();
            }
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            if (!isRunning) { return; }
            isRunning = false;
            Notification.Remove(ref notification);
            if (particles != null) {
                s.Sys.Particles.RemoveCustomParticles(particles);
                particles.Destroy();
                particles = null;
            }
            if (eventDef.Event is BaseSpaceStormStoryEvent storm) {
                storm.StopStormEffects(s, eventDef);
            }
            var ctx = s.Query.GetStoryContext.Ask();
            ctx.ClearCooldown(StoryEventFlags.SpaceStorm);
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
            // A freshly loaded game starts with no storm running
            isRunning = false;
            OnEnterSector(s, sector);
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
            if (IsSwitchCheckTick(ticks)) {
                var isDisabled = IsDisabled(s);
                if (isRunning && isDisabled) {
                    OnExitSector(s, currentSector);
                } else if (!isRunning && !isDisabled) {
                    OnEnterSector(s, currentSector);
                }
            }
            if (!isRunning) { return; }
            if (eventDef.Event is BaseSpaceStormStoryEvent storm) {
                storm.TickStormEffects(s, eventDef);
            }
            if (ticks % Consts.TicksPer15Minutes == 7) {
                var ctx = s.Query.GetStoryContext.Ask();
                ctx.AutoCoolDownFlag(StoryEventFlags.SpaceStorm, Consts.TicksPer3Days);
            }
        }
    }
}
