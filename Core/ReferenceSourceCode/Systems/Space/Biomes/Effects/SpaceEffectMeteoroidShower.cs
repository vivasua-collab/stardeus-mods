using Game.Audio;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Systems.Story;
using Game.Systems.Voice;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectMeteoroidShower : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectMeteoroidShower());
        }

        // Don't change or saves will break
        public override string Id => "MeteorShower";
        public override string Name => T.EvtMeteorShowerTitle;
        protected override string StoryEventId => "Space/MeteoroidShower";
        private NotificationHandle warningEvent;
        private long nextHit;
        private bool isDisabled;
        private IThreat threat;
        private void EnsureThreat(GameState s) {
            if (threat == null) {
                threat = new ShieldableSpaceThreat();
                s.Sig.RegisterThreat.Send(threat, true);
            }

        }
        private void RemoveThreat(GameState s) {
            if (threat != null) {
                s.Sig.RegisterThreat.Send(threat, false);
                threat = null;
            }
        }
        protected override void Cleanup() {
            threat = null;
            warningEvent = default;
            nextHit = 0;
            isDisabled = false;
        }
        private void EnsureWarningEvent(GameState s) {
            if (warningEvent.IsValid) { return; }
            EnsureThreat(s);
            Sounds.PlayMusic(SoundtrackMood.Danger, true);
            s.Clock.DangerPause();
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            warningEvent = Notification.Build(IconId.CWarning, AddSectorPrefix(T.EvtMeteorShowerTitle),
                    0, NotificationType.Alert)
                .WithTooltip(T.EvtMeteorShowerDesc)
                .Sticky()
                .Emergency(false)
                .WithVoice(VoiceLine.MeteoroidShower)
                .Show();
            var title = AddSectorPrefix(Texts.Red(T.EvtMeteorShowerTitle));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
        }

        public override void OnEnterSector(GameState s, SpaceSector sector) {
            isDisabled = IsStoryEventDisabled(s);
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            if (warningEvent.IsValid) {
                RemoveThreat(s);
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
                // Disabled mid-shower: the warning goes, and the next shower starts over
                OnExitSector(s, currentSector);
                nextHit = 0;
                return;
            }
            if (nextHit == 0) {
                nextHit = NextHitTime(s, ticks, currentSector);
                if (nextHit > ticks + Consts.TicksPer2Hours) {
                    nextHit = ticks + Consts.TicksPer2Hours;
                }
                return;
            }
            if (nextHit < ticks) {
                EnsureWarningEvent(s);
                nextHit = NextHitTime(s, ticks, currentSector);
                Hit(s, ticks, currentSector, Rng.Unseeded);
            }
        }

        public void Hit(GameState S, long ticks, SpaceSector sector, Rng rng) {
            // base 3-5 wave, raised (never cut to nothing) by sector and game difficulty
            var scale = 1f + (sector.Biome.Difficulty - 0.5f)
                + (S.Difficulty.SoftLevel - 1f) * 0.5f;
            var meteors = Mathf.Clamp(Mathf.RoundToInt(rng.Range(3f, 5f) * scale), 1, 10);
            MeteoroidSalvo.Spawn(S, ticks, rng.From(Facing.Types), meteors, 35f, rng);
        }


        private static readonly Curve nextHitTimeCurve = new float[] {
            0f, Consts.TicksPer5Minutes,
            0.1f, Consts.TicksPer15Minutes,
            0.15f, Consts.TicksPer1Hours,
            0.5f, Consts.TicksPer2Hours,
            0.9f, Consts.TicksPer3Hours,
            1f, Consts.TicksPer6Hours
        };

        private long NextHitTime(GameState s, long ticks, SpaceSector sector) {
            var inc = s.Difficulty.Increment;
            var red = s.Difficulty.Reduction;
            var delay = nextHitTimeCurve.Evaluate(Rng.UValue);
            // harder sectors hit more often; the difficulty branches below still apply
            delay *= Mathf.Lerp(1f, 0.5f, sector.Biome.Difficulty);
            if (red < 1f) {
                //lower difficulty - increase the minimum delay
                var minDelay = Mathf.Lerp(Consts.TicksPer2Hours, Consts.TicksPer5Minutes, red);
                if (delay < minDelay) {
                    delay = minDelay;
                }
            } else if (inc > 1f) {
                // higher difficulty - reduce max delay
                //lower difficulty - increase the minimum delay
                var minDelay = Consts.TicksPer2Hours;
                if (delay > minDelay) {
                    delay = minDelay / inc;
                }
            }

            return ticks + (int) delay;
        }
    }
}
