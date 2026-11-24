using System;
using Game.Audio;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
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
    public sealed class MeteoroidShowerStoryEvent : StoryEvent {
        public const string EventId = "MeteoroidShower";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new MeteoroidShowerStoryEvent());
        }

        private Curve salvoSizeCurve;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            // prevent magnetic storm x meteoroids
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
            salvoSizeCurve ??= CurveDef.FastGet(
                CurveIdH.StoryEvents_SectorDifficulty_MeteoroidShowerSalvoSize);

            // Log entry and notification
            data.CreateLogEntry(GraphicsId.ObjAsteroidsAsteroid01,
                T.EvtMeteorShowerTitle, T.EvtMeteorShowerDesc);
            data.ShowPopup();
            ShowEventNotification(data);
            var title = Texts.Red(T.EvtMeteorShowerTitle);
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
            EnsureThreat(s, data);

            // wave count is set by game difficulty, wave size by sector difficulty
            var sectorDiff = ctx.CurrentSector.Biome.Difficulty;
            var salvos = Mathf.Clamp(Mathf.RoundToInt(3f * s.Difficulty.SoftLevel), 3, 10);
            var perSalvo = Mathf.Clamp(Mathf.RoundToInt(
                salvoSizeCurve.Evaluate(sectorDiff) * rng.Range(0.8f, 1.2f)), 3, 40);
            vars.SetInt("DirectionFrom", (int) rng.From(Facing.Types));
            vars.SetInt("SalvosLeft", salvos);
            vars.SetInt("MeteorsPerSalvo", perSalvo);
            vars.SetLong("NextSalvoTick", ctx.Ticks);
            D.Warn("Meteoroid shower: {0} salvos of {1} meteoroids. Sector diff: {2}",
                salvos, perSalvo, sectorDiff);

            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            Sounds.PlayMusic(SoundtrackMood.Danger, true);
            s.Clock.DangerPause();
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

            if ((ctx.Ticks - data.StartTick) > Consts.TicksPer12Hours) {
                D.Warn("Ending meteoroid shower at the 12 hour hard stop. {0} salvos left",
                    vars.GetInt("SalvosLeft", 0));
                data.SetState(s, StoryEventState.Ending);
                return;
            }

            if (ctx.Ticks < vars.GetLong("NextSalvoTick", 0L)) {
                return;
            }
            if (vars.GetInt("SalvosLeft", 0) > 0) {
                SpawnSalvo(ctx, data);
            } else {
                // tail elapsed - the last wave has landed
                data.SetState(s, StoryEventState.Ending);
            }
        }

        private void SpawnSalvo(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var vars = data.Data;
            var rng = data.SeededRng;

            var salvosLeft = vars.GetInt("SalvosLeft", 0) - 1;
            vars.SetInt("SalvosLeft", salvosLeft);

            var perSalvo = vars.GetInt("MeteorsPerSalvo", 5);
            var dir = (Facing.Type) vars.GetInt("DirectionFrom", 0);
            MeteoroidSalvo.Spawn(s, ctx.Ticks, dir, perSalvo, 15f, rng);
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            D.Warn("Meteoroid shower salvo incoming: {0} meteoroids, {1} salvos left. Sector diff: {2}",
                perSalvo, salvosLeft, ctx.CurrentSector.Biome.Difficulty);

            vars.SetLong("NextSalvoTick", ctx.Ticks + (salvosLeft > 0
                ? rng.Range(Consts.TicksPer30Minutes, Consts.TicksPer60Minutes)
                : Consts.TicksPer1Hours));
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            RemoveThreat(s, data);
            data.RemoveNotification();
            Sounds.PlayMusic(SoundtrackMood.Calm, false);
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            ShowEventNotification(data);
            EnsureThreat(ctx.S, data);
        }

        private static void ShowEventNotification(StoryEventData data) {
            data.Event = Notification.Build(GraphicsId.ObjAsteroidsAsteroid02,
                    T.EvtMeteorShowerTitle, 0, NotificationType.Critical)
                .WithOnClick(data.ShowPopup)
                .Sticky()
                .Emergency(false)
                .WithVoice(VoiceLine.MeteoroidShower)
                .Show();
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
