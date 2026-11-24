using Game.CodeGen;
using Game.Data;
using Game.Systems.Items;
using KL.Randomness;
using KL.Utils;

namespace Game.Scenarios {
    // Solar panels are cleaned from space, and a crew without robots has nobody who flies.
    // One crew member starts in a Jet Suit instead, picked by what the panel cleaning goal
    // asks of them, so it goes to someone who can clean whenever anyone can.
    // StartingCrewJobs keeps a random crew from rolling without such a person.
    public static class StartingJetSuit {
        public static void GiveToCleaner(GameState s, Rng rng) {
            var wearer = s.Beings.FindFirstMatching(b => IsCandidate(b) && CanClean(b))
                ?? s.Beings.FindFirstMatching(IsCandidate);
            if (wearer == null) {
                D.Warn("Nobody in the starting crew can wear a jet suit");
                return;
            }
            if (!CanClean(wearer)) {
                D.Warn("Nobody in the starting crew can clean, {0} gets the jet suit", wearer);
            }
            JetSuits.TryEquip(s, wearer.Data, rng);
        }

        private static bool IsCandidate(Being b) {
            return b.IsAlive && b.Data.IsPlayer && JetSuits.HasSuitSlot(b.Data);
        }

        // The cleaning goal's job and required abilities, the flying comes with the suit
        private static bool CanClean(Being b) {
            return b.Data.Mind.Jobs.CanDo(JobTypeIdH.Cleaning, out string _)
                && b.Data.HasAbility(AbilityIdH.CleanDevice)
                && b.Data.HasAbility(AbilityIdH.DumbLabor);
        }
    }
}
