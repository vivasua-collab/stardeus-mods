using System;
using Game.Audio;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Notify;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Systems.Voice;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class PoliceRaidStoryEvent : StoryEvent {
        public const string EventId = "PoliceRaid";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new PoliceRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return !A.IsInHyperspace && !A.Player.Ship.IsDerelict
                && (!A.Player.Ship.IsOutlaw
                    || !SpaceEffectMagneticStorm.IsOngoing(ctx.S));
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (A.Player.Ship.IsDerelict) {
                data.FailEvent(s, "Player is derelict", isHardFailure: false);
                return;
            }

            bool isPlayerOutlaw = A.Player.Ship.IsOutlaw;
            if (isPlayerOutlaw) {
                new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
                Sounds.PlayMusic(SoundtrackMood.Combat, true);
                s.Clock.DangerPause();
                data.CreateLogEntry(IconId.CPoliceBadge, T.EvPoliceRaidTitle,
                    T.EvPoliceRaidDesc);
                data.TargetSO = ctx.CurrentSector.SO;
                s.Sig.ShowFanfare.Send(Texts.Red(T.EvPoliceRaidTitle));
                s.Sys.Log.AddLine(Texts.Red(T.EvPoliceRaidTitle));
                EnsureWarningEvent(ctx, data);
            } else {
                data.HideLogEntry();
            }
            SpawnPoliceShips(ctx, isPlayerOutlaw);
            data.SetState(s, StoryEventState.Ending);
        }

        private void SpawnPoliceShips(StoryContext ctx, bool targetPlayer) {
            var s = ctx.S;
            int shipCount = Rng.Unseeded.Range(2, 4); // 2-3 ships
            for (int i = 0; i < shipCount; i++) {
                var tpl = new NPCShip();
                tpl.IsPolice = true;
                // Once the Sentinel is gone there is nobody left to police a rogue AI on
                // their behalf, so enforcement falls back to Old Earth.
                var faction = A.HasAsimovOverride && !A.SentinelEliminated
                    ? FactionIdH.Sentinel : FactionIdH.OldEarth;
                tpl.Faction = FactionDef.FastGet(faction);
                var policeShip = s.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, tpl);
                if (targetPlayer) {
                    NPCShipBehavior_Sector_Combat.AssignTo(policeShip, A.Player.Ship);
                } else {
                    NPCShipBehavior_Sector_Orbit.AssignTo(policeShip);
                }
            }
        }

        private void EnsureWarningEvent(StoryContext ctx, StoryEventData data) {
            Notification.Build(IconId.CPoliceBadge, Texts.Red(T.EvPoliceRaidTitle), Consts.TicksPer4Hours, NotificationType.Critical)
                .WithOnClick(data.ShowPopup)
                .Emergency(false)
                .WithVoice(VoiceLine.EnemyShip)
                .Show();
        }
    }
}
