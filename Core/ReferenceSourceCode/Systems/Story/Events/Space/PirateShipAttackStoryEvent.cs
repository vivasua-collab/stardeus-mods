using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using UnityEngine;
using Game.Audio;
using Game.Systems.Notify;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class PirateShipAttackStoryEvent : StoryEvent {
        public const string EventId = "PirateShipAttack";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new PirateShipAttackStoryEvent());
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
            var ship = SpawnPirateShip(ctx);
            data.TargetSO = ship.SO;
            data.CreateLogEntry(IconId.IconsFactionPirate,
                T.EvPirateAttackTitle, T.EvPirateAttackDesc);
            EnsureWarningEvent(ctx, data, ship);
            data.SetState(s, StoryEventState.Ending);
        }

        private NPCShip SpawnPirateShip(StoryContext ctx) {
            var s = ctx.S;
            var tpl = new NPCShip();
            tpl.Faction = FactionDef.FastGet(FactionIdH.Pirate);
            var pirateShip = s.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, tpl);

            // Reposition the pirate ship to be at least half the exit radius away from the player
            var parent = A.Player.Parent;
            var playerLoc = A.Player.Location;
            var minDistance = parent.ExitDist * 0.5f;

            // Generate a random location that's at least minDistance away from the player
            var loc = GenerateSafeSpawnLocation(parent, playerLoc, minDistance, s.Rng);
            pirateShip.TeleportToSectorLocation(loc);

            NPCShipBehavior_Sector_Combat.AssignTo(pirateShip, A.Player.Ship);
            return pirateShip;
        }

        private Vector2 GenerateSafeSpawnLocation(SpaceObject parent, Vector2 playerLoc,
                float minDistance, Rng rng) {
            var maxDist = parent.ExitDist;

            // If minDistance is larger than the available space, clamp it
            if (minDistance > maxDist) {
                minDistance = maxDist * 0.8f;
            }

            // Try to find a valid position within a reasonable number of attempts
            const int maxAttempts = 20;
            for (int i = 0; i < maxAttempts; i++) {
                var loc = SOUtils.RandomLocationInside(parent, rng);
                var distToPlayer = Vector2.Distance(loc, playerLoc);
                D.Warn("Dist to player: {0}, Max distance: {1}", distToPlayer, maxDist);

                if (distToPlayer >= minDistance) {
                    return loc;
                }
            }

            // If we couldn't find a good location randomly, place it on a ring at minDistance
            // from the player in a random direction
            var direction = rng.InsideUnitCircle();
            if (direction.sqrMagnitude < 0.01f) {
                direction = Vector2.right;
            } else {
                direction = direction.normalized;
            }

            var desiredLoc = playerLoc + direction * minDistance;

            // Make sure we're still within the parent's bounds
            if (desiredLoc.magnitude > maxDist) {
                // If the desired location is outside bounds, try the opposite direction
                desiredLoc = playerLoc - direction * minDistance;

                // If that's also outside bounds, place at the maximum available distance
                // from the player in the direction away from sector center
                if (desiredLoc.magnitude > maxDist) {
                    var awayFromCenter = -playerLoc;
                    if (awayFromCenter.sqrMagnitude < 0.01f) {
                        awayFromCenter = Vector2.right;
                    } else {
                        awayFromCenter = awayFromCenter.normalized;
                    }
                    desiredLoc = playerLoc + awayFromCenter * Mathf.Min(minDistance,
                        maxDist - playerLoc.magnitude);
                }
            }

            return desiredLoc;
        }

        private void EnsureWarningEvent(StoryContext ctx, StoryEventData data,
                NPCShip ship) {
            var s = ctx.S;
            var title = Texts.Red(Texts.LinkToShip(ship, T.EvPirateAttackTitle));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(title);
            data.Event = data.AddEventNotification(ctx, IconId.IconsFactionPirate,
                Texts.Red(T.EvPirateAttackTitle),
                NotificationType.Alert, showPopup: false,
                onClick: () => s.Sys.Universe.ShowInStarmap(ship.SO));
            data.ShowPopup();
        }
    }
}
