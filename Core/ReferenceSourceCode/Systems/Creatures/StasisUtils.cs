using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Drama;
using Game.Systems.Mental;
using KL.Utils;

namespace Game.Systems.Creatures {
    // Shared logic for colonists manually sent back into cryo-stasis (see
    // AIActionEnterStasis / StasisArrayTab) and later offered back as a wake-up choice by
    // StasisWakeUpStoryEvent. Identity mirrors DeserterUtils's shape: a ColonyRole + a
    // "when did this happen" tick on Mind, found later via a linear scan of AllBeingData -
    // not a roster stored on any specific StasisArrayComp. Chamber occupancy (BeingCount),
    // however, is tracked per-array like anonymous cryo-mass: the entering array's
    // StoreColonist() claims a chamber, and whichever array wakes them later releases one.
    public static class StasisUtils {
        // All beings currently sent into stasis, earliest-sent first (so a batch of wake-up
        // choices consumes them in a stable order).
        public static List<BeingData> FindReturnableSleepers(GameState s) {
            var result = new List<BeingData>();
            foreach (var bd in s.Query.AllBeingData.Ask().Values) {
                if (bd.Mind?.Role != ColonyRole.InStasis) { continue; }
                result.Add(bd);
            }
            result.Sort((a, b) => a.Mind.StasisSentTick.CompareTo(b.Mind.StasisSentTick));
            return result;
        }

        // Total occupied chambers (anonymous cryo-mass and sent-back colonists alike),
        // summed across every StasisArrayComp in the colony.
        public static int TotalStoredBeingCount(GameState s) {
            int total = 0;
            var arrays = s.Components.AllByType<StasisArrayComp>();
            if (arrays != null) {
                foreach (var c in arrays) {
                    total += ((StasisArrayComp) c).BeingCount;
                }
            }
            return total;
        }

        // Called once a being sent to stasis actually reaches the StasisArray's InPort.
        // Only ever invoked on confirmed arrival (see AIActionEnterStasis.Execute) - a
        // being whose journey fails or is interrupted never reaches here, so nothing about
        // it is ever touched.
        // The array is the one whose chamber they took (StoreColonist): a sleeper belongs to a
        // specific array, which is what its Manifest tab lists and what its stability harms.
        public static void SendToStasis(GameState s, BeingData data, StasisArrayComp array,
                long ticks) {
            // Cryo sickness is what coming out of a pod does to you, not a state of being in
            // one. Going back in clears it; the next wake-up applies a fresh one from that
            // array's stability (StasisArrayComp.ApplyWakeUpEffects).
            data.RemoveCondition(s, ConditionIdH.CryoSickness);
            LeaveEquipment(data);
            s.Query.AssignBedSlot.Ask(data, null);
            s.Query.AssignBotDock.Ask(data, null);
            data.Mind.Role = ColonyRole.InStasis;
            data.Mind.StasisSentTick = ticks;
            data.Mind.StasisArrayId = array?.EntityId ?? 0;
            data.StateFlags |= BeingState.Hidden;
            data.Body.Flags |= BodyFlags.Missing;

            ApplyBondMoodEffect(s, data.Mind, IconId.CCryogenics, ticks,
                T.MoodFriendSentToStasis, -8);
            TrustDeeds.FriendSentToStasis(s, data.Mind);
            array?.NotifyManifestChanged();
        }

        // A sleeper is Missing, and ObjsSys deletes whatever a Missing owner wears. Nobody
        // knows when they wake again, so their gear stays at the array for the colony.
        private static void LeaveEquipment(BeingData data) {
            var inv = data.Inventory;
            if (inv == null) { return; }
            foreach (var slot in inv.Equipment) {
                if (slot.Value.Item != null) {
                    slot.Value.RemoveItem();
                }
            }
        }

        // Never woken: the player knows who this is, not what they can do (see
        // ManifestStatus). Drives what the entity panel shows and which wake-up path runs.
        public static bool IsUnrevealed(BeingData data) {
            return data.Mind?.ManifestStatus == ManifestStatus.Sleeping;
        }

        public static StasisArrayComp ArrayOf(GameState s, BeingData data) {
            var id = data.Mind?.StasisArrayId ?? 0;
            if (id == 0) { return null; }
            return s.Components.Find<StasisArrayComp>(id);
        }

        // Any sleeper, whichever way they got into the pod
        public static Being Wake(GameState s, BeingData data, StasisArrayComp array,
                bool allowRaid, long ticks) {
            return IsUnrevealed(data)
                ? WakeManifestSleeper(s, data, array, allowRaid)
                : WakeReturningSleeper(s, data, array, ticks);
        }

        // First wake-up of a manifest sleeper. They were registered without ever being on
        // the map (BeingsSys.RegisterDormant), so first-time init has not happened yet.
        // Going back to Uninitialized right before the spawn makes BeingsSys.Activate run
        // it exactly as it does for anyone who appears for the first time.
        public static Being WakeManifestSleeper(GameState s, BeingData data,
                StasisArrayComp array, bool allowRaid) {
            var mind = data.Mind;
            var arrayIdBefore = mind.StasisArrayId;
            var flagsBefore = data.StateFlags;
            data.Body.Flags &= ~BodyFlags.Missing;
            data.StateFlags = BeingState.Uninitialized;
            mind.Role = ColonyRole.Unassigned;
            mind.StasisArrayId = 0;

            var being = array.SpawnBeing(data, allowRaid);
            if (being == null) {
                // Still asleep as far as everything else is concerned
                data.Body.Flags |= BodyFlags.Missing;
                data.StateFlags = flagsBefore;
                mind.Role = ColonyRole.InStasis;
                mind.StasisArrayId = arrayIdBefore;
                return null;
            }
            mind.ManifestStatus = ManifestStatus.Woken;
            ManifestSys.For(s).NoteWoken();
            array.NotifyManifestChanged();
            return being;
        }

        // Called by StasisWakeUpStoryEvent when the chosen wake-up candidate is a
        // previously sent-away colonist rather than a freshly generated stranger.
        public static Being WakeReturningSleeper(GameState s, BeingData data,
                StasisArrayComp array, long ticks) {
            var arrayIdBefore = data.Mind.StasisArrayId;
            data.Body.Flags &= ~BodyFlags.Missing;
            data.StateFlags &= ~BeingState.Hidden;
            data.Mind.Role = ColonyRole.Unassigned;
            data.Mind.StasisArrayId = 0;

            var being = array.SpawnKnownBeing(data);
            if (being == null) {
                data.Body.Flags |= BodyFlags.Missing;
                data.StateFlags |= BeingState.Hidden;
                data.Mind.Role = ColonyRole.InStasis;
                data.Mind.StasisArrayId = arrayIdBefore;
                return null;
            }
            ManifestSys.For(s).NoteWoken();
            array.NotifyManifestChanged();

            ApplyBondMoodEffect(s, data.Mind, IconId.CParty, ticks,
                T.MoodFriendReturnedFromStasis, +8);
            TrustDeeds.FriendWoken(s, data.Mind);
            return being;
        }

        // Shared with SendToStasis's negative mood hit - same "loop all beings, find
        // whoever has this Mind in one of their Important bonds, apply a fresh effect"
        // shape as DramaSys.OnAshesForgotten, positive or negative depending on the caller.
        // A nemesis isn't a "close relationship" (see BeingBonds.IsCloseTo).
        private static void ApplyBondMoodEffect(GameState s, Mind subject, string icon,
                long ticks, string reason, int effect) {
            foreach (var bd in s.Query.AllBeingData.Ask().Values) {
                if (bd.Mind == subject || !bd.IsPlayer) { continue; }
                if (bd.Mind?.Health == null) { continue; }
                if (bd.HasStateFlag(BeingState.Dead)) { continue; }
                var bonds = bd.Mind.Bonds;
                if (bonds == null || !bonds.IsCloseTo(subject)) { continue; }
                var pos = bd.Being?.PosIdx ?? -1;
                var eff = MentalEffect.CreateWithIcon(pos, icon, ticks,
                    Consts.TicksPer3Days, reason, effect);
                s.Sig.AddMentalEffect.Send(bd, eff);
            }
        }
    }
}
