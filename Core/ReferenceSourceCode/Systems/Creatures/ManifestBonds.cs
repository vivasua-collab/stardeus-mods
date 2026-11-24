using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Backstories;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Story;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Creatures {
    // A stasis array holds people who shared a ship long before the player met them, so their
    // opinions should read like years of history: a few real bonds, and everyone else an
    // acquaintance with feelings still moving. The drama generator rolls every pair on its own,
    // which produced almost no mutual friends (the Friend flag was rolled per side) and no cap
    // on marriages. So after the households get their opinions, the important bonds of the
    // whole array are dealt from one budget here:
    //  - 0-1 married couple, 0-1 lovers, 0-2 crushes: adult man and woman who are attracted to
    //    each other, with a sensible age gap
    //  - 1-2 pairs of friends and 1 nemesis
    // Each person gets at most one of these, so the bonds spread across the manifest. Only
    // pairs that already know each other (same household) are candidates.
    //
    // Every bond is one the game could have produced on its own: each flag sits where
    // BeingOpinion would promote it, and no value is above the ceiling the pair's attraction
    // or affinity allows (OpinionLimits). When the attraction or affinity a pair was rolled
    // does not allow the bond, it is rolled again by the same generator, as if the pair had
    // simply been rolled closer (or further apart). A pair no roll allows it for is passed
    // over. A bond the game could not produce does not hold anyway: love without attraction
    // is taken away on load (BeingOpinion.Validate), and a value above its ceiling never grows.
    public static class ManifestBonds {
        private const BeingOpinionFlags Budgeted = BeingOpinionFlags.Friend
            | BeingOpinionFlags.Crush | BeingOpinionFlags.Lover
            | BeingOpinionFlags.Spouse | BeingOpinionFlags.Nemesis;

        // Developing values are kept under the thresholds that promote a flag on their own
        // (see DramaSystem.md), or the first interaction after waking would hand out bonds
        // the budget never granted
        private const float MaxDevelopingFriendship = 0.7f;
        private const float MaxDevelopingLove = 0.24f;
        private const float MaxDevelopingHate = 0.95f;

        private const int SpouseMaxAgeGap = 10;
        private const int LoverMaxAgeGap = 15;
        private const int CrushMaxAgeGap = 20;

        // Dealt love. The minimum is what the attraction has to allow; a crush needs more
        // than OpinionLimits.CrushAboveLove to be promoted on its own.
        private const float SpouseLoveMin = 0.8f;
        private const float SpouseLoveMax = 1f;
        private const float LoverLoveMin = 0.5f;
        private const float LoverLoveMax = 0.8f;
        private const float CrushLoveMin = 0.3f;
        private const float CrushLoveMax = 0.5f;

        // Generator rolls per side before a pair is passed over. A likely bond comes up within
        // a few; an unlikely one may not, and that pair is treated like one it is impossible for.
        private const int Rolls = 32;

        private struct Pair {
            public BeingData A;
            public BeingData B;
            public BeingOpinion AB;
            public BeingOpinion BA;
            public bool IsHumans;
        }

        public static void Apply(GameState s, List<BeingData> sleepers, Rng rng) {
            var all = CollectPairs(sleepers);
            if (all.Count == 0) { return; }
            // Every pair loses its rolled bonds, pets included - a human's opinion of a pet
            // got the same one-sided Friend / Nemesis rolls. Only people are dealt new ones.
            var pairs = new List<Pair>(all.Count);
            foreach (var p in all) {
                Reset(p.AB);
                Reset(p.BA);
                if (p.IsHumans) {
                    pairs.Add(p);
                }
            }
            var taken = new HashSet<uint>();

            // Rarest first, so a couple is not blocked by a friendship that took one of them
            if (rng.Chance(0.5f)) {
                Deal(pairs, taken, rng, 1, p => TryMakeSpouses(s, p, rng));
            }
            if (rng.Chance(0.5f)) {
                Deal(pairs, taken, rng, 1, p => TryMakeLovers(s, p, rng));
            }
            // Then the bonds every array has (friends, a nemesis) before the optional crushes:
            // dealt last, the nemesis found everyone in a small array already taken
            Deal(pairs, taken, rng, rng.Range(1, 3), p => TryMakeFriends(s, p, rng));
            DealNemesis(s, pairs, taken, rng);
            Deal(pairs, taken, rng, rng.Range(0, 3),
                p => TryMakeCrush(s, p.A, p.B, p.AB, rng) || TryMakeCrush(s, p.B, p.A, p.BA, rng));

            foreach (var bd in sleepers) {
                bd.Mind?.Bonds?.RebuildImportant();
            }
        }

        private static List<Pair> CollectPairs(List<BeingData> sleepers) {
            var pairs = new List<Pair>();
            for (int i = 0; i < sleepers.Count; i++) {
                var a = sleepers[i];
                var bondsA = a.Mind?.Bonds;
                if (bondsA == null) { continue; }
                for (int j = i + 1; j < sleepers.Count; j++) {
                    var b = sleepers[j];
                    var bondsB = b.Mind?.Bonds;
                    if (bondsB == null) { continue; }
                    if (!bondsA.Opinions.TryGetValue(b.Mind.Id, out var ab)) { continue; }
                    if (!bondsB.Opinions.TryGetValue(a.Mind.Id, out var ba)) { continue; }
                    // Pets keep the Master/Pet bond the household generator gave them, it is
                    // not part of the budget
                    var isHumans = a.Species.IsHumanoid && b.Species.IsHumanoid;
                    pairs.Add(new Pair { A = a, B = b, AB = ab, BA = ba, IsHumans = isHumans });
                }
            }
            return pairs;
        }

        private static void Reset(BeingOpinion op) {
            var remove = op.Flags & Budgeted;
            if (remove != BeingOpinionFlags.None) {
                foreach (var flag in new[] { BeingOpinionFlags.Friend, BeingOpinionFlags.Crush,
                        BeingOpinionFlags.Lover, BeingOpinionFlags.Spouse,
                        BeingOpinionFlags.Nemesis }) {
                    if ((remove & flag) == flag) {
                        op.RemoveFlag(flag);
                    }
                }
            }
            if (op.Friendship > MaxDevelopingFriendship) { op.Friendship = MaxDevelopingFriendship; }
            if (op.Hate > MaxDevelopingHate) { op.Hate = MaxDevelopingHate; }
            if (op.Love > MaxDevelopingLove) { op.Love = MaxDevelopingLove; }
        }

        // Offers up to `count` untaken pairs, in random order, to `tryDeal`, which bonds the
        // pair when it can and leaves it untouched when it cannot
        private static void Deal(List<Pair> pairs, HashSet<uint> taken, Rng rng, int count,
                System.Func<Pair, bool> tryDeal) {
            if (count < 1) { return; }
            var order = Shuffled(pairs.Count, rng);
            foreach (var idx in order) {
                if (count < 1) { return; }
                var p = pairs[idx];
                if (taken.Contains(p.A.Id) || taken.Contains(p.B.Id)) { continue; }
                if (!tryDeal(p)) { continue; }
                taken.Add(p.A.Id);
                taken.Add(p.B.Id);
                count--;
            }
        }

        // The nemesis is the pair that dislikes each other most, not a random one: the next
        // one down when no affinity roll lets that one get there
        private static void DealNemesis(GameState s, List<Pair> pairs, HashSet<uint> taken,
                Rng rng) {
            var order = new List<int>(pairs.Count);
            for (int i = 0; i < pairs.Count; i++) {
                var p = pairs[i];
                if (taken.Contains(p.A.Id) || taken.Contains(p.B.Id)) { continue; }
                order.Add(i);
            }
            order.Sort((x, y) => {
                var byAffinity = LowerAffinity(pairs[x]).CompareTo(LowerAffinity(pairs[y]));
                return byAffinity != 0 ? byAffinity : x.CompareTo(y);
            });
            foreach (var idx in order) {
                var p = pairs[idx];
                // The one who dislikes the other more is the hater; half the time it is mutual
                var aHates = p.AB.SocialAffinity <= p.BA.SocialAffinity;
                var hater = aHates ? p.A : p.B;
                var other = aHates ? p.B : p.A;
                if (!TryMakeNemesis(s, hater, other, aHates ? p.AB : p.BA, rng)) { continue; }
                if (rng.Chance(0.5f)) {
                    TryMakeNemesis(s, other, hater, aHates ? p.BA : p.AB, rng);
                }
                taken.Add(p.A.Id);
                taken.Add(p.B.Id);
                return;
            }
        }

        private static float LowerAffinity(Pair p) {
            return System.Math.Min(p.AB.SocialAffinity, p.BA.SocialAffinity);
        }

        private static List<int> Shuffled(int count, Rng rng) {
            var order = new List<int>(count);
            for (int i = 0; i < count; i++) { order.Add(i); }
            for (int i = count - 1; i > 0; i--) {
                var j = rng.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        private static bool IsAdult(BeingData bd) {
            var years = (uint) Mind.AgeYears(bd.Mind.Age);
            return bd.Species.AgePhaseFor(years) != AgePhase.Childhood;
        }

        private static int AgeGap(BeingData a, BeingData b) {
            return System.Math.Abs(Mind.AgeYears(a.Mind.Age) - Mind.AgeYears(b.Mind.Age));
        }

        private static bool IsAttractedTo(BeingData who, BeingData to) {
            var sex = to.Mind.Sex;
            return sex != null && Arrays.ContainsSafe(who.Mind.AttractedToSex, sex);
        }

        private static bool IsManAndWoman(BeingData a, BeingData b) {
            var sa = a.Mind.Sex;
            var sb = b.Mind.Sex;
            if (sa == null || sb == null) { return false; }
            return (sa.TypeChar == 'M' && sb.TypeChar == 'F')
                || (sa.TypeChar == 'F' && sb.TypeChar == 'M');
        }

        // Lovers start as two crushes and a confession (ConfessLoveStoryEvent), and spouses are
        // lovers who married, so somebody has to be able to confess
        private static bool IsRomanticMatch(Pair p, int maxAgeGap) {
            if (p.A.Species != p.B.Species) { return false; }
            if (!IsManAndWoman(p.A, p.B)) { return false; }
            if (!IsAdult(p.A) || !IsAdult(p.B)) { return false; }
            if (AgeGap(p.A, p.B) > maxAgeGap) { return false; }
            if (p.A.Body.MaxLibido < ConfessLoveStoryEvent.MinLibidoToInitiate
                    && p.B.Body.MaxLibido < ConfessLoveStoryEvent.MinLibidoToInitiate) {
                return false;
            }
            return IsAttractedTo(p.A, p.B) && IsAttractedTo(p.B, p.A);
        }

        // A crush is one-sided: only the one with the crush has to be attracted
        private static bool IsCrushMatch(BeingData who, BeingData to) {
            if (who.Species != to.Species || !IsManAndWoman(who, to)) { return false; }
            if (!IsAdult(who) || !IsAdult(to)) { return false; }
            if (AgeGap(who, to) > CrushMaxAgeGap) { return false; }
            return IsAttractedTo(who, to);
        }

        // The value a pair already has when it allows the bond, else a new roll of the same
        // generator that does. False when none of the rolls did.
        private static bool TryRoll(float current, System.Func<float, bool> allows,
                System.Func<float> roll, out float value) {
            value = current;
            for (int i = 0; !allows(value); i++) {
                if (i == Rolls) { return false; }
                value = roll();
            }
            return true;
        }

        private static bool TryAttraction(GameState s, BeingData who, BeingData to,
                BeingOpinion op, float love, Rng rng, out float attraction) {
            // OnLoveChanged promotes nothing without attraction, whatever the ceiling allows
            return TryRoll(op.SexualAttraction,
                a => a > 0f && OpinionLimits.MaxLove(a) >= love,
                () => s.Query.RollSexualAttraction.Ask(who, to, rng), out attraction);
        }

        private static bool TryAffinity(GameState s, BeingData who, BeingData to,
                BeingOpinion op, System.Func<float, bool> allows, Rng rng, out float affinity) {
            return TryRoll(op.SocialAffinity, allows,
                () => s.Query.RollSocialAffinity.Ask(who.Mind, to.Mind, rng), out affinity);
        }

        // From the design range, but never above what the pair's ceiling allows
        private static float Dealt(Rng rng, float min, float max, float ceiling) {
            if (ceiling <= min) { return ceiling; }
            return rng.Range(min, System.Math.Min(max, ceiling));
        }

        private static bool TryMakeSpouses(GameState s, Pair p, Rng rng) {
            if (!IsRomanticMatch(p, SpouseMaxAgeGap)
                    || !TryAttraction(s, p.A, p.B, p.AB, SpouseLoveMin, rng, out var ab)
                    || !TryAttraction(s, p.B, p.A, p.BA, SpouseLoveMin, rng, out var ba)) {
                return false;
            }
            foreach (var (op, attraction) in new[] { (p.AB, ab), (p.BA, ba) }) {
                op.SexualAttraction = attraction;
                op.Love = Dealt(rng, SpouseLoveMin, SpouseLoveMax, OpinionLimits.MaxLove(attraction));
                op.Friendship = System.Math.Max(op.Friendship,
                    Dealt(rng, 0.3f, 0.7f, OpinionLimits.MaxFriendship(op.SocialAffinity)));
                op.TrySetFlag(BeingOpinionFlags.Spouse);
            }
            return true;
        }

        private static bool TryMakeLovers(GameState s, Pair p, Rng rng) {
            if (!IsRomanticMatch(p, LoverMaxAgeGap)
                    || !TryAttraction(s, p.A, p.B, p.AB, LoverLoveMin, rng, out var ab)
                    || !TryAttraction(s, p.B, p.A, p.BA, LoverLoveMin, rng, out var ba)) {
                return false;
            }
            foreach (var (op, attraction) in new[] { (p.AB, ab), (p.BA, ba) }) {
                op.SexualAttraction = attraction;
                op.Love = Dealt(rng, LoverLoveMin, LoverLoveMax, OpinionLimits.MaxLove(attraction));
                op.TrySetFlag(BeingOpinionFlags.Lover);
            }
            return true;
        }

        private static bool TryMakeCrush(GameState s, BeingData who, BeingData to,
                BeingOpinion op, Rng rng) {
            if (!IsCrushMatch(who, to)
                    || !TryAttraction(s, who, to, op, CrushLoveMin, rng, out var attraction)) {
                return false;
            }
            op.SexualAttraction = attraction;
            op.Love = Dealt(rng, CrushLoveMin, CrushLoveMax, OpinionLimits.MaxLove(attraction));
            op.TrySetFlag(BeingOpinionFlags.Crush);
            return true;
        }

        private static bool TryMakeFriends(GameState s, Pair p, Rng rng) {
            if (!TryAffinity(s, p.A, p.B, p.AB, AllowsFriend, rng, out var ab)
                    || !TryAffinity(s, p.B, p.A, p.BA, AllowsFriend, rng, out var ba)) {
                return false;
            }
            foreach (var (op, affinity) in new[] { (p.AB, ab), (p.BA, ba) }) {
                op.SocialAffinity = affinity;
                op.Friendship = Dealt(rng, OpinionLimits.FriendFromFriendship, 1f,
                    OpinionLimits.MaxFriendship(affinity));
                op.Hate = 0f;
                op.TrySetFlag(BeingOpinionFlags.Friend);
            }
            return true;
        }

        private static bool TryMakeNemesis(GameState s, BeingData who, BeingData to,
                BeingOpinion op, Rng rng) {
            if (!TryAffinity(s, who, to, op, AllowsNemesis, rng, out var affinity)) {
                return false;
            }
            op.SocialAffinity = affinity;
            op.Hate = OpinionLimits.NemesisAtHate;
            op.Friendship = 0f;
            op.TrySetFlag(BeingOpinionFlags.Nemesis);
            return true;
        }

        private static bool AllowsFriend(float affinity) {
            return OpinionLimits.MaxFriendship(affinity) >= OpinionLimits.FriendFromFriendship;
        }

        private static bool AllowsNemesis(float affinity) {
            return OpinionLimits.MaxHate(affinity) >= OpinionLimits.NemesisAtHate;
        }
    }
}
