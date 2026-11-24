using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Drama;

namespace Game.Systems.Mental {
    /// <summary>
    /// Trust effects for deeds the AI does to one colonist, felt by the people close to them.
    /// Called from where each deed happens; TrustSys owns the colony-wide and standing ones.
    /// </summary>
    public static class TrustDeeds {
        public const string ReasonFriendToStasis = "trust.friend.to.stasis";
        public const string ReasonFriendWoken = "trust.friend.woken";
        public const string ReasonNoCeremony = "trust.friend.no.ceremony";
        public const string ReasonFriendChipped = "trust.friend.chipped";
        public const string ReasonTakenBack = "trust.taken.back";

        public static void FriendSentToStasis(GameState s, Mind subject) {
            GiveBonded(s, subject, TrustSourceIdH.FriendToStasis, Tunables.TrustFriendToStasis,
                Tunables.TrustFriendToStasisDays, ReasonFriendToStasis);
        }

        public static void FriendWoken(GameState s, Mind subject) {
            GiveBonded(s, subject, TrustSourceIdH.FriendWoken, Tunables.TrustFriendWoken,
                Tunables.TrustFriendWokenDays, ReasonFriendWoken);
        }

        public static void FriendLeftWithoutCeremony(GameState s, Mind subject) {
            GiveBonded(s, subject, TrustSourceIdH.NoCeremony, Tunables.TrustNoCeremony,
                Tunables.TrustNoCeremonyDays, ReasonNoCeremony);
        }

        // Only a crewmate: chipping a captured raider is not done to one of their own
        public static void PartInstalled(GameState s, BodyPartSlot slot) {
            if (slot.InstalledDef?.Def?.IdH != DefIdsH.ObjPartsImplantObeyChip) { return; }
            var owner = slot.Body.Owner;
            if (owner?.Mind == null || !TrustSys.IsColonist(owner)) { return; }
            GiveBonded(s, owner.Mind, TrustSourceIdH.FriendChipped, Tunables.TrustFriendChipped,
                Tunables.TrustFriendChippedDays, ReasonFriendChipped);
        }

        public static void TakenBack(BeingData deserter) {
            if (!TrustSys.IsColonist(deserter)) { return; }
            deserter.Mind.Trust.Add(TrustSourceIdH.TakenBack, Tunables.TrustTakenBack, G.Ticks,
                (long) Tunables.TrustTakenBackDays * Consts.TicksPerDay, ReasonTakenBack);
        }

        // Friends, lovers and family, the bonds StasisUtils.ApplyBondMoodEffect also uses.
        // A nemesis is not close (BeingBonds.IsCloseTo).
        private static void GiveBonded(GameState s, Mind subject, int sourceIdH, int points,
                int days, string reasonKey) {
            if (subject == null) { return; }
            var ticks = G.Ticks;
            var duration = (long) days * Consts.TicksPerDay;
            foreach (var bd in s.Query.AllBeingData.Ask().Values) {
                if (bd.Mind == subject || !TrustSys.IsColonist(bd)) { continue; }
                var bonds = bd.Mind.Bonds;
                if (bonds == null || !bonds.IsCloseTo(subject)) { continue; }
                bd.Mind.Trust.Add(sourceIdH, points, ticks, duration, reasonKey);
            }
        }
    }
}
