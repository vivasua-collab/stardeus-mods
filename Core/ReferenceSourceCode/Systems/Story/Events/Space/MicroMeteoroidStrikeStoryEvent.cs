using System;
using Game.Audio;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Systems.Voice;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Space;

namespace Game.Systems.Story {
    public sealed class MicroMeteoroidStrikeStoryEvent : StoryEvent {
        public const string EventId = "MicroMeteoroidStrike";
        public static readonly int EventIdH = Hashes.S(EventId);
        public const int maxAttempts = 1000;
        private Curve sectorDifficultyStrikeCurve;
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new MicroMeteoroidStrikeStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            // prevent magnetic storm x micro meteoroids
            if (ctx.IsEventRunning(MagneticStormStoryEvent.EventIdH, 1.5f)) {
                return false;
            }
            if (ctx.HasSectorEffect(SpaceEffectMagneticStorm.EffectIdH, 1.5f)) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var rng = data.SeededRng;
            var vars = data.GetOrCreateData();
            sectorDifficultyStrikeCurve ??= CurveDef.FastGet(CurveIdH.StoryEvents_SectorDifficulty_MicroMeteoroidHitCount);

            // Initialize state
            var directionFrom = rng.From(Facing.Types);
            var minHits = sectorDifficultyStrikeCurve.Evaluate(ctx.CurrentSector.Biome.Difficulty)
                * s.Difficulty.Level;
            if (minHits < 3) {
                minHits = 3;
            } else if (minHits > 100) {
                minHits = 100;
            }

            vars.SetInt("DirectionFrom", (int) directionFrom);
            vars.SetInt("MinHits", (int) minHits);
            vars.SetInt("HitCount", 0);
            data.SetState(s, StoryEventState.Ongoing);
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;

            if (A.IsInHyperspace) {
                data.SetState(s, StoryEventState.Ending);
                return;
            }

            var vars = data.Data;
            if (vars == null) {
                data.FailEvent(s, "Data is null");
                return;
            }

            var nextHitTick = vars.GetLong("NextHitTick", 0L);
            if ((ctx.Ticks - data.StartTick) > Consts.TicksPer3Days) {
                D.Warn("Ending micro meteoroid strike after 3 days soft timeout. {0}/{1} hits. Next hit would be in: {2}",
                    vars.GetInt("HitCount", 0), vars.GetInt("MinHits", 0),
                    Units.TicksToTime(nextHitTick - ctx.Ticks));
                data.SetState(s, StoryEventState.Ending);
                return;
            }

            if (G.Ticks > nextHitTick) {
                var warningShown = data.Event.IsValid;

                if (!warningShown) {
                    new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
                    Sounds.PlayMusic(SoundtrackMood.Danger, true);
                    s.Clock.DangerPause();

                    ShowWarningEvent(ctx, data);
                    vars.SetLong("NextHitTick", G.Ticks + data.SeededRng.Range(
                        Consts.TicksPer60Minutes, Consts.TicksPer90Minutes));

                } else {
                    PerformHit(ctx, data);
                }
            }
        }

        private void ShowWarningEvent(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;

            EnsureThreat(s, data);

            data.Event = Notification.Build(IconId.CWarning, T.EvtMicroMeteorsTitle, 0, NotificationType.Critical)
                .WithOnClick(data.ShowPopup)
                .Sticky()
                .Emergency(false)
                .WithVoice(VoiceLine.MicroMeteoroids)
                .Show();
            var title = Texts.Red(T.EvtMicroMeteorsTitle);
            if (!data.LogEntry.IsValid) {
                data.CreateLogEntry(IconId.CWarning, T.EvtMicroMeteorsTitle,
                    T.EvtMicroMeteorsDesc);
                s.Sys.Log.AddLine(title);
            }
            s.Sig.ShowFanfare.Send(title);
        }

        private void PerformHit(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var vars = data.Data;
            var rng = data.SeededRng;

            var hitCount = vars.GetInt("HitCount", 0);
            var minHits = vars.GetInt("MinHits", 3);
            var attempts = vars.GetInt("Attempts", 0);
            attempts++;
            vars.SetInt("Attempts", attempts);
            D.Warn("Hits: {0}/{1}. Attempts: {2}", hitCount, minHits, attempts);
            var directionFrom = (Facing.Type) vars.GetInt("DirectionFrom", 0);

            var count = Pos.GridW / 32;
            var dir = Facing.Rotate(directionFrom, Facing.Opposite(Facing.FromAngle(s.Sys.Ship.Rotation)));
            var posF = EntityUtils.MeteorVector(dir, 10f, count, rng, out var strikeV);

            var hit = SpaceEffectMicroMeteoroids.PerformHit(G.Ticks, rng, s,
                    count, posF, strikeV, out var hitShield);
            if (hit) {
                // we want to hammer the shield a little more than just the ship
                bool registerHit = hitShield ? rng.Chance(0.5f) : true;
                if (registerHit) {
                    hitCount++;
                    vars.SetInt("HitCount", hitCount);
                }
            } else {
                if (rng.Chance(0.05f)) {
                    minHits--;
                    D.Warn("Did not hit, but rolled a 5% chance to register a hit");
                    vars.SetInt("MinHits", minHits);
                }
            }

            if (hitCount >= minHits || attempts > maxAttempts || ((ctx.Ticks - data.StartTick) > Consts.TicksPer12Days)) {
                data.SetState(s, StoryEventState.Ending);
            } else {
                if (rng.Chance(0.75f)) {
                    vars.SetLong("NextHitTick", G.Ticks +
                        rng.Range(Consts.TicksPer3Minutes, Consts.TicksPer30Minutes));
                } else {
                    vars.SetLong("NextHitTick", G.Ticks +
                        rng.Range(Consts.TicksPer45Minutes, Consts.TicksPer60Minutes));
                }
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;

            RemoveThreat(s, data);

            data.RemoveNotification();

            s.Sig.ShowFanfare.Send(Texts.Green(T.EvtMicroMeteorsEnded));
            s.Sys.Log.AddLine(T.EvtMicroMeteorsEnded, Texts.TMPColorGreen);
            Sounds.PlayMusic(SoundtrackMood.Calm, false);
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            ShowWarningEvent(ctx, data);
        }
        private void EnsureThreat(GameState s, StoryEventData data) {
            if (data.TempObj is not ShieldableSpaceThreat threat) {
                threat = new ShieldableSpaceThreat();
                data.TempObj = threat;
                s.Sig.RegisterThreat.Send(threat, true);
            }

        }
        private void RemoveThreat(GameState s, StoryEventData data) {
            if (data.TempObj is ShieldableSpaceThreat threat) {
                s.Sig.RegisterThreat.Send(threat, false);
                data.TempObj = null;
            }
        }

    }
}
