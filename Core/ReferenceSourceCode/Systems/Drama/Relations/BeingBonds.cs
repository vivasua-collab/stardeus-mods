using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Mental;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Collections;
using KL.Utils;

namespace Game.Systems.Drama {
    public sealed class BeingBonds {

        /// <summary>
        /// Returns true if this opinion can be silently removed: no Important bond, and the
        /// opinion has not changed since before staleBeforeTick.
        /// LastChangedTick == 0 (old saves / never stamped) counts as maximally stale.
        /// </summary>
        public static bool IsForgettable(BeingOpinion op, long staleBeforeTick) {
            if ((op.Flags & BeingOpinionFlags.Important) != BeingOpinionFlags.None) { return false; }
            return op.LastChangedTick < staleBeforeTick;
        }

        /// <summary>
        /// Removes all forgettable opinions (acquaintance-only, stale) from this bonds object.
        /// Returns number of entries removed.
        /// </summary>
        public int ForgetStaleAcquaintances(long staleBeforeTick) {
            if (Opinions.Count == 0) { return 0; }
            List<uint> toRemove = null;
            foreach (var kv in Opinions) {
                if (IsForgettable(kv.Value, staleBeforeTick)) {
                    (toRemove ??= new List<uint>()).Add(kv.Key);
                }
            }
            if (toRemove == null) { return 0; }
            foreach (var id in toRemove) { Opinions.Remove(id); }
            // No RebuildImportant() needed: removed opinions had no Important flag.
            return toRemove.Count;
        }

        /// <summary>
        /// These will be cached in Mind.Bonds.Important dictionary for quick access.
        /// If this is changed, update BeingOpinionFlags.Important too!
        /// </summary>
        private static BeingOpinionFlags[] importantFlags = new[] {
            BeingOpinionFlags.Friend,
            BeingOpinionFlags.Crush,
            BeingOpinionFlags.Lover,
            BeingOpinionFlags.Nemesis,
            BeingOpinionFlags.Master,
            BeingOpinionFlags.Pet,
            BeingOpinionFlags.Spouse,
        };

        public Mind Owner;
        /// <summary>
        /// Mind id => opinion
        /// </summary>
        public readonly Dictionary<uint, BeingOpinion> Opinions = new();
        public readonly Dictionary<BeingOpinionFlags, FrugalArray<Mind>> Important = new();

        public readonly BCallback Changed = new();

        /// <summary>
        /// Whenever this being has a social interaction, we will register the last time
        /// it happened. That way we can say if there at most 3 interactions within last
        /// day, we will cool it down for a while.
        /// </summary>
        public RingBuffer<long> LastSocialInteractionTimes = new(10, 0L);

        public void RegisterInteraction(long ticks) {
            LastSocialInteractionTimes.Push(ticks);
        }

        public int SocialInteractionsSince(long ticks, out long last) {
            int count = 0;
            last = 0;
            for (int i = LastSocialInteractionTimes.Size - 1; i >= 0; i--) {
                var time = LastSocialInteractionTimes.Buffer[i];
                if (time > last) { last = time; }
                if (time > 0 && time >= ticks) { count++; }
            }
            return count;
        }

        public static void AnnounceNew(GameState s, Mind b1, Mind b2, BeingOpinionFlags bond) {
            var msg = BondMessage(bond, b1, b2);
            if (msg == null) { return; }
            // s.Sig.ShowFanfare.Send(msg);
            s.Sys.Log.AddLine(msg);
            s.Sig.BeingBondCreated.Send(b1, b2, bond);
            Achievements.DramaAchievements.OnBondCreated(bond);
        }

        public static string BondMessage(BeingOpinionFlags bond, Mind m1, Mind m2) {
            switch (bond) {
                case BeingOpinionFlags.Friend: return T.StatusFriendsAnnounce_V2.F(
                    m1.LinkToShortName, m2.LinkToShortName);
                case BeingOpinionFlags.Lover: return T.StatusLoversAnnounce_V2.F(
                    m1.LinkToShortName, m2.LinkToShortName);
                case BeingOpinionFlags.Nemesis: return T.StatusNemesisAnnounce_V2.F(
                    m1.LinkToShortName, m2.LinkToShortName);
                case BeingOpinionFlags.Spouse: return T.StatusSpouseAnnounce_V2.F(
                    m1.LinkToShortName, m2.LinkToShortName);
                case BeingOpinionFlags.Master: return null;
                case BeingOpinionFlags.Pet: return T.StatusPetAnnounce_V2.F(
                    m1.LinkToShortName, m2.LinkToShortName);
            }
            D.Err("Unhandled BeingOpinionFlags bond message: {0}", bond);
            return null;
        }

        public static string BondName(BeingOpinionFlags bond) {
            switch (bond) {
                case BeingOpinionFlags.Friend: return T.StatusFriends;
                case BeingOpinionFlags.Lover: return T.StatusLovers;
                case BeingOpinionFlags.Nemesis: return T.StatusNemesis;
                case BeingOpinionFlags.Spouse: return T.StatusSpouse;
                case BeingOpinionFlags.Master: return T.StatusMaster;
                case BeingOpinionFlags.Pet: return T.FlagPet;
            }
            D.Err("Unhandled BeingOpinionFlags name: {0}", bond);
            return bond.ToString();
        }

        public static void EnsureFor(Mind mind) {
            if (mind.Bonds == null) {
                mind.Bonds = new BeingBonds();
                mind.Bonds.Owner = mind;
            }
        }

        // Friends, crushes, lovers, family and pets. Flags stack, so a nemesis can still carry
        // a one-sided Friend flag (a developing friendship): next to Nemesis it only counts
        // when the friendship is mutual.
        public bool IsCloseTo(Mind other) {
            if (other == null || !Opinions.TryGetValue(other.Id, out var op)) { return false; }
            var close = op.Flags & BeingOpinionFlags.Important & ~BeingOpinionFlags.Nemesis;
            if (close == BeingOpinionFlags.None) { return false; }
            if ((op.Flags & BeingOpinionFlags.Nemesis) == BeingOpinionFlags.None) { return true; }
            if ((close & ~BeingOpinionFlags.Friend) != BeingOpinionFlags.None) { return true; }
            return Owner != null && other.Bonds != null
                && other.Bonds.Opinions.TryGetValue(Owner.Id, out var theirs)
                && op.AreFriendsWith(theirs);
        }

        public void RebuildImportant() {
            Important.Clear();
            foreach (var op in Opinions.Values) {
                for (int i = importantFlags.Length - 1; i >= 0; i--) {
                    var f = importantFlags[i];
                    if ((op.Flags & f) == f) {
                        AddImportant(f, op);
                    }
                }
            }
        }

        public void AddImportant(BeingOpinionFlags flag, BeingOpinion op) {
            var mind = op.TargetMind;
            if (mind == null) {
                D.Err("Mind not found by id when adding important: {0} -> {1} | for {2}",
                    flag, op.TargetMindId, Owner);
                return;
            }
            if (!Important.TryGetValue(flag, out var list)) {
                list = new FrugalArray<Mind>();
            }
            // D.Warn("Adding important bond: {0} => {1} (for {2})", flag, mind, Owner);
            list.Add(op.TargetMind);
            // set back the struct
            Important[flag] = list;

        }

        public void RemoveImportant(BeingOpinionFlags flag, BeingOpinion op) {
            if (Important.TryGetValue(flag, out var list)) {
                var mind = op.TargetMind;
                if (mind == null) {
                    D.Err("Mind not found by id {0} when removing important bond: {1} | for {2}",
                        op.TargetMindId, flag, Owner);
                    return;
                }
                if (!list.Contains(op.TargetMind)) {
                    D.Err("Mind was not found in list of important flags: {0}: {1} | for: {2}",
                        flag, op.TargetMindId, Owner);
                }
                var newList = new FrugalArray<Mind>();
                for (int i = 0; i < list.Length; i++) {
                    var m = list[i];
                    if (m != mind) {
                        newList.Add(m);
                    }
                }
                Important[flag] = newList;
            }
        }
    }
}
