using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Audio;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class VirusShipAttackStoryEvent : StoryEvent {
        public const string EventId = "VirusShipAttack";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new VirusShipAttackStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return !A.Player.Ship.IsDerelict
                && !SpaceEffectMagneticStorm.IsOngoing(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (A.IsInHyperspace) {
                data.SetState(ctx.S, StoryEventState.Ongoing);
            } else {
                OnTick(ctx, data);
            }
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (A.Player.Ship.IsDerelict) {
                data.FailEvent(s, "Player's ship is a derelict",
                    isHardFailure: false);
                return;
            }
            if (SpaceEffectMagneticStorm.IsOngoing(s)) {
                data.FailEvent(s, "Magnetic storm ongoing",
                    isHardFailure: false);
                return;
            }
            if (A.IsInHyperspace) {
                // wait for player to emerge
                return;
            }
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);

            Sounds.PlayMusic(SoundtrackMood.Combat, true);
            s.Clock.DangerPause();
            var ship = SpawnShip(ctx, data);
            data.TargetSO = ship.SO;
            data.CreateLogEntry(IconId.IconsFactionVirus,
                T.EvVirusAttackTitle, T.EvVirusAttackDesc);
            EnsureWarningEvent(ctx, data, ship);
            data.SetState(s, StoryEventState.Ending);
        }

        private NPCShip SpawnShip(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var tpl = new NPCShip();
            tpl.Faction = FactionDef.FastGet(FactionIdH.Virus);
            var ship = s.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, tpl);
            var dist = data.Def.Vars.Get(PropertyIdH.Range, 5);
            ship.TeleportToSectorLocation(A.Player.Location
                + ctx.Rng.InsideUnitCircle().normalized * dist);
            NPCShipBehavior_Sector_Combat.AssignTo(ship, A.Player.Ship);
            return ship;
        }

        private void EnsureWarningEvent(StoryContext ctx, StoryEventData data,
                NPCShip ship) {
            var s = ctx.S;
            var title = Texts.Red(Texts.LinkToShip(ship, T.EvVirusAttackTitle));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
            var ev = data.AddEventNotification(ctx, IconId.IconsFactionVirus,
                Texts.Red(T.EvVirusAttackDesc),
                NotificationType.Alert,
                showPopup: true,
                onClick: () => s.Sys.Universe.ShowInStarmap(ship.SO));
        }
    }
}
