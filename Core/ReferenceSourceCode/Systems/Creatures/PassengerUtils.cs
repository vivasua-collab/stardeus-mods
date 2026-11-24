using System.Collections.Generic;
using Game.Data;
using Game.Data.Factions;

namespace Game.Systems.Creatures {
    /// <summary>
    /// Lifecycle helpers for guest passengers (delivery quest visitors of a non-hostile,
    /// different faction). All exits — delivery, quest completion/failure, death, and
    /// defection — route through <see cref="EndGuestRole"/> so cleanup is consistent and
    /// idempotent.
    /// </summary>
    public static class PassengerUtils {
        /// <summary>
        /// Quest-level ComponentData key under which the delivery quest stores the passenger's
        /// mind id. Plain (non step-prefixed) so all of the quest's steps can read it.
        /// </summary>
        public const string PassengerMindIdKey = "passenger.mindid";

        private const int RescueBoostDays = 3;

        // Rescued from a derelict, a guest is happy to be alive for a while. Only a rescue:
        // a passenger who boards from a ship or station for a delivery was never in danger.
        public static void AddRescueBoostFor(BeingData data) {
            BeingUtils.AddInitialMentalBoostFor(data, RescueBoostDays);
        }

        /// <summary>
        /// True for an alive guest physically aboard the player's ship, as opposed to one
        /// still sitting in a wreck (same IsWithinMargin idiom that separates ship from
        /// derelict corner elsewhere).
        /// </summary>
        public static bool IsGuestAboard(BeingData data) {
            if (data?.Being == null || !data.Being.IsAlive || !data.IsGuest) { return false; }
            return EntityUtils.IsWithinMargin(data.Being.PosIdx, false, false);
        }

        // An alive guest still on a wreck, outside the ship margin. The player may carry
        // it aboard (STA-1730); once aboard it is no longer relocatable.
        public static bool IsGuestOnDerelict(BeingData data) {
            if (data?.Being == null || !data.Being.IsAlive || !data.IsGuest) { return false; }
            return !EntityUtils.IsWithinMargin(data.Being.PosIdx, false, false);
        }

        /// <summary>
        /// Collects guests of the given faction that can be handed back home via the
        /// "Send Beings" hail action: aboard the player's ship (see <see cref="IsGuestAboard"/>)
        /// and not required by an active quest (delivery passengers are locked in). Returns
        /// the number found, appended into <paramref name="into"/>.
        /// </summary>
        public static int CollectSendableGuests(GameState s, FactionDef faction,
                List<BeingData> into) {
            if (faction == null) { return 0; }
            int found = 0;
            var beings = s.Beings.Alive;
            for (int i = 0; i < beings.Count; i++) {
                var data = beings[i].Data;
                if (data == null || data.Mind.Faction != faction || !IsGuestAboard(data)) {
                    continue;
                }
                if (s.Query.IsMindRequiredByQuest.Ask(data.Mind.Id)) { continue; }
                into?.Add(data);
                found++;
            }
            return found;
        }

        /// <summary>
        /// Ends a being's guest passenger role. Idempotent: a no-op if the being is null or
        /// not a passenger.
        /// <para>
        /// When <paramref name="despawn"/> is true (delivered, quest ended while aboard) the
        /// being is removed from the ship (which also unassigns its bed). When false (defected
        /// to a raid of their own faction) the being stays alive: its bed is freed and it is
        /// re-grouped (it is no longer a guest, so it falls into the Enemy group).
        /// </para>
        /// </summary>
        public static bool EndGuestRole(GameState s, BeingData data, bool despawn) {
            if (data?.Mind == null || data.Mind.Role != ColonyRole.Passenger) { return false; }
            data.Mind.Role = ColonyRole.Unassigned;
            if (despawn) {
                // Removes the being and unassigns bed + dock as part of cleanup.
                BeingUtils.RemoveFromShip(s, data);
            } else {
                // The role has no change notification: the side changes as a faction change
                // would. Drops the Friendly / Guest nav bits, so Enemy-denying doors block the
                // defector, and turns its green guest name red.
                data.Mind.AfterSetFaction(false);
                // Free the bed and re-classify into the (now correct) Enemy group.
                s.Query.AssignBedSlot.Ask(data, null);
                BeingGroup.AssignFor(data);
                s.Sig.RebuildBeingGroups.Send();
            }
            return true;
        }
    }
}
