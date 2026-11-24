using Game.Audio;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Systems.Notify;
using Game.Systems.Story;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectSpaceDiarrhea : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectSpaceDiarrhea());
        }

        public override string Id => "SpaceDiarrhea";
        public override string Name => T.AlienBacteriaDetected;
        protected override string StoryEventId => "Main/SpaceDiarrhea";
        private NotificationHandle warningEvent;
        private long nextHit;
        private bool isDisabled;
        protected override void Cleanup() {
            warningEvent = default;
            nextHit = 0;
            isDisabled = false;
        }
        private void EnsureWarningEvent(GameState s) {
            if (warningEvent.IsValid) { return; }
            Sounds.PlayMusic(SoundtrackMood.Danger, true);
            s.Clock.DangerPause();
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            warningEvent = Notification.Build(IconId.CWarning, AddSectorPrefix(T.AlienBacteriaDetected),
                    0, NotificationType.Warning)
                .WithTooltip(T.EvtSpaceDiarrheaDesc)
                .Sticky()
                .Show();
            var title = AddSectorPrefix(Texts.Yellow(T.AlienBacteriaDetected));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            isDisabled = IsStoryEventDisabled(s);
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            if (warningEvent.IsValid) {
                Notification.Remove(ref warningEvent);
            }
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
            isDisabled = IsStoryEventDisabled(s);
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
            if (IsSwitchCheckTick(ticks)) {
                isDisabled = IsStoryEventDisabled(s);
            }
            if (isDisabled) {
                // Disabled mid-sector: the warning goes, and the next infection is planned anew
                OnExitSector(s, currentSector);
                nextHit = 0;
                return;
            }
            if (nextHit == 0) {
                nextHit = NextInfectTime(ticks, 1f);
                return;
            }
            if (nextHit < ticks) {
                EnsureWarningEvent(s);
                nextHit = NextInfectTime(ticks, s.Difficulty.Level);

                SpaceDiarrheaStoryEvent.InfectOneColonist(s);
            }
        }

        private long NextInfectTime(long ticks, float diff) {
            return ticks + Mathf.CeilToInt(Consts.TicksPer3Days / diff);
        }
    }
}
