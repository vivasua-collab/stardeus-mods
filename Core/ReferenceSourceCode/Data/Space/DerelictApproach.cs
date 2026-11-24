using Game.Commands;
using Game.Constants;
using UnityEngine;

namespace Game.Data.Space {
    /// <summary>
    /// Approaching a derelict is a real flight: the ship has to fly to the target and only
    /// boards it upon arrival. That is what makes the bridge hail an operator and the engines
    /// burn, and it is what lets the player cancel an approach through "Cancel Flight".
    /// The flight itself is owned by ShipSys - this only decides where to fly and what to do
    /// once we are there.
    /// </summary>
    public static class DerelictApproach {
        /// <summary>
        /// Far away: fly straight onto the derelict. Already next to it: a tiny maneuver
        /// towards it, just big enough to be seen and to turn the ship a little.
        /// </summary>
        public static Vector2 FlightPosFor(GameState s, SpaceObject so) {
            var from = s.Universe.Player.Location;
            so.Ship.NearbyPosFrom(from, out var isNear);
            if (!isNear) {
                return so.Location;
            }
            var dir = so.Location - from;
            if (dir.sqrMagnitude < 0.0001f) {
                // we are standing exactly on it, any direction will do
                dir = s.Sys.ShipNav.ShipForward;
            } else {
                dir = dir.normalized;
            }
            return from + dir * Tunables.DerelictApproachHopDist;
        }

        public static void Begin(GameState s, SpaceObject so) {
            s.Sys.Ship.SetApproachDestination(so, FlightPosFor(s, so));
        }

        /// <summary>
        /// The actual boarding. Runs instantly in dev mode, otherwise once the approach
        /// flight has arrived, which can be many hours after it was ordered.
        /// </summary>
        public static void Execute(GameState s, SpaceObject so) {
            if (so?.Ship?.IsDerelict != true) {
                s.Sig.ShowErrorMessage.Send(
                    $"{T.OperationAborted}: {T.ObjectNoLongerAvailable}");
                return;
            }
            if (s.Sys.Expedition.FindForSpaceObject(so) != null) {
                s.Sig.ShowErrorMessage.Send(
                    $"{T.OperationAborted}: {T.SpaceObjectApproachFailExpeditionRunning}");
                return;
            }

            var questTargets = s.Query.GetQuestStepTargets.Ask(
                SOUtils.QuestProviderFor(so));
            if (questTargets.Length > 0 && questTargets.First.OnInteract != null) {
                questTargets.First.OnInteract();
                return;
            }

            s.CmdQ.Enqueue(new CmdSpawnDerelictShip(so));
        }
    }
}
