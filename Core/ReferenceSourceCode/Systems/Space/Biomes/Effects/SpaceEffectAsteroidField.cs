using System.Buffers;
using Game.Audio;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class SpaceEffectAsteroidField : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectAsteroidField());
        }

        public override string Id => "AsteroidField";
        public override string Name => T.SpaceObjectAsteroidzone;
        protected override string StoryEventId => "Space/Asteroid";
        private NotificationHandle warningEvent;
        private long nextHit;
        private bool isDisabled;
        protected override void Cleanup() {
            warningEvent = default;
            nextHit = 0;
            isDisabled = false;
        }
        private void EnsureWarningEvent(GameState s) {
            if (warningEvent.IsValid) {
                return;
            }
            Sounds.PlayMusic(SoundtrackMood.Danger, true);
            s.Clock.DangerPause();
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            warningEvent = Notification.Build(IconId.CWarning,
                    AddSectorPrefix(T.EvtAsteroidTitle), 0, NotificationType.Critical)
                .Sticky()
                .WithTooltipFunc(() => T.EvtAsteroidDesc_V1.F(Units.TicksToTime(nextHit - G.Ticks)))
                .Emergency(false)
                .WithVoice(VoiceLine.Asteroid)
                .Show();
            var title = AddSectorPrefix(Texts.Red(T.EvtAsteroidTitle));
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
                // Disabled mid-field: the warning goes, and the next hit is planned anew
                OnExitSector(s, currentSector);
                nextHit = 0;
                return;
            }
            if (nextHit >= ticks) { return; }
            if (nextHit == 0) {
                nextHit = NextHitTime(s, ticks);
                if (nextHit > ticks + Consts.TicksPer6Hours) {
                    nextHit = ticks + Consts.TicksPer6Hours;
                }
                return;
            }
            EnsureWarningEvent(s);
            nextHit = NextHitTime(s, ticks);
            D.Warn("next hit time: {0}", Units.TicksToTime(nextHit - ticks));
            Hit(s, ticks, Rng.Unseeded);
        }

        public void Hit(GameState s, long ticks, Rng rng) {
            if (!Ready.AreasInitial) {
                D.Err("Initial areas not ready, skipping asteroid!");
                return;
            }

            var values = s.Sys.Areas.SolidArea;
            if (Arrays.IsEmpty(values)) {
                D.Err("No solid area for asteroid to target!");
                return;
            }
            Vector2 hitPosition;
            var size = values.Count;
            var pool = ArrayPool<int>.Shared;
            var allPositions = pool.Rent(size);
            for (int i = 0; i < size; i++) {
                allPositions[i] = values[i];
            }
            Arrays.Shuffle(rng, allPositions, size);
            int target = Pos.Invalid;
            bool foundGoodTarget = false;
            // Stasis array, ship computer
            EntityUtils.LoadImportantTilesCache();
            for (int i = 0; i < size; i++) {
                target = allPositions[i];
                // D.Warn("Checking position #{0}: {1}", i, Pos.String(target));
                if (!EntityUtils.IsTooCloseToAnyOf(target, 12, Caches.TileList)) {
                    foundGoodTarget = true;
                    break;
                }
            }
            if (!foundGoodTarget) {
                D.Err("Could not find good target for astroid in all {0} positions!", size);
                target = rng.From(values);
                target = RetargetEmptySpace(s, rng, target);
            }
            pool.Return(allPositions);

            // D.Warn("Will hit position #{0}", Pos.String(target));
            // We have a target, let's remember it
            hitPosition = EntityUtils.CenterOf(target);
            var options = The.Defs.Matching(DefIds.ObjAsteroidsAsteroid01);
            var chosen = Rng.UFrom(options);
            // D.Warn("Chosen asteroid {0} from options: {1}", chosen, D.DumpCollection(options));
            s.CmdQ.Enqueue(new CmdSpawnObj(hitPosition, chosen, true));
        }
        private int RetargetEmptySpace(GameState s, Rng rng, int oldTarget) {
            var newTarget = oldTarget;
            for (int i = 0; i < s.GridWidth; i++) {
                newTarget = Pos.E(newTarget);
                if (!EntityUtils.IsWithinBounds(newTarget)) {
                    // Returning original target, we went OOB
                    return oldTarget;
                }
                // it's a space
                if (EntityUtils.GetTopmostTile(newTarget) == null) {
                    if (rng.Chance(0.5f)) {
                        // Pretty far miss
                        newTarget = Pos.NeighborAt(newTarget, -rng.Range(10, 30), 0);
                    } else {
                        // Near miss
                        newTarget = Pos.NeighborAt(newTarget, -rng.Range(1, 5), 0);
                    }
                    return newTarget;
                }
            }
            D.Err("WTF. Should return earlier");
            return newTarget;
        }
        private static readonly Curve nextHitTimeCurve = new float[] {
            0f, Consts.TicksPer3Hours,
            0.1f, Consts.TicksPer6Hours,
            0.15f, Consts.TicksPer12Hours,
            0.5f, Consts.TicksPer24Hours,
            0.9f, Consts.TicksPer2Days,
            1f, Consts.TicksPer3Days
        };

        private long NextHitTime(GameState s, long ticks) {
            var inc = s.Difficulty.Increment;
            var red = s.Difficulty.Reduction;
            var delay = nextHitTimeCurve.Evaluate(Rng.UValue);
            if (red < 1f) {
                //lower difficulty - increase the minimum delay
                var minDelay = Mathf.Lerp(Consts.TicksPer24Hours, Consts.TicksPer3Hours, red);
                if (delay < minDelay) {
                    delay = minDelay;
                }
            }

            return ticks + (int) delay;
        }

    }
}
