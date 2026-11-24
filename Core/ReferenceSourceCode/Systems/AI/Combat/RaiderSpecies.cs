using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    /// <summary>
    /// Which species a faction's raiders (and hostile derelict crews, gifted beings) are drawn
    /// from. The faction only decides *how many* of them are bots (FactionDef.AI); *which* bots
    /// is a function of raid strength through the same 0-2 tier weapons use
    /// (Species.RaidTierWeights), so a day-5 pirate raid rolls Carriers and Cleaning Bots, not
    /// the Sentries the same faction fields at day 500.
    /// </summary>
    public static class RaiderSpecies {
        public const int MaxTier = 2;

        /// <summary>
        /// Fills the weighted pool for one raider draw: bots weighted by their tier weight and
        /// rescaled to sum to the faction's AI share, Human takes the rest. With flyingOnly,
        /// biological species other than Human are dropped (any bot can be given a MiniJet, see
        /// BeingUtils.EnsureFlying; a Human has to roll a jet suit, which is the caller's job).
        /// Bots in except are left out, as if their tier weight were 0 (a raid drops the bots
        /// its cost cap keeps rejecting, see RaiderPicker).
        /// </summary>
        public static void WeightedFor(FactionDef fac, int gearTier, bool flyingOnly,
                Dictionary<Species, float> into, HashSet<Species> except = null) {
            into.Clear();
            var ai = Mathf.Clamp01(fac?.AI ?? 0f);
            if (ai > 0f) {
                AddBots(gearTier, flyingOnly, ai, into, except);
            }
            if (ai < 1f) {
                into[Species.Get(SpeciesIdH.Human)] = 1f - ai;
            }
        }

        public static void BotsOnlyFor(int gearTier, bool flyingOnly,
                Dictionary<Species, float> into, HashSet<Species> except = null) {
            into.Clear();
            AddBots(gearTier, flyingOnly, 1f, into, except);
        }

        private static void AddBots(int gearTier, bool flyingOnly, float totalWeight,
                Dictionary<Species, float> into, HashSet<Species> except) {
            gearTier = Mathf.Clamp(gearTier, 0, MaxTier);
            // An empty tier (every bot of that tier filtered out) cascades to the tier below
            // rather than to nothing, so a raid never silently turns all-human or all-Drone.
            for (int tier = gearTier; tier >= 0; tier--) {
                float sum = 0f;
                foreach (var sp in Species.Known.Values) {
                    var w = TierWeight(sp, tier, flyingOnly, except);
                    if (w <= 0f) { continue; }
                    sum += w;
                }
                if (sum <= 0f) { continue; }
                foreach (var sp in Species.Known.Values) {
                    var w = TierWeight(sp, tier, flyingOnly, except);
                    if (w <= 0f) { continue; }
                    into[sp] = w / sum * totalWeight;
                }
                return;
            }
            into[Species.Get(SpeciesIdH.Drone)] = totalWeight;
        }

        private static float TierWeight(Species sp, int tier, bool flyingOnly,
                HashSet<Species> except) {
            if (flyingOnly && !CanBeMadeFlying(sp)) { return 0f; }
            if (except != null && except.Contains(sp)) { return 0f; }
            return TierWeight(sp, tier);
        }

        public static float TierWeight(Species sp, int tier) {
            var w = sp.RaidTierWeights;
            if (w == null || tier < 0 || tier >= w.Length) { return 0f; }
            return w[tier];
        }

        public static bool CanBeMadeFlying(Species sp) {
            return !sp.IsBiological;
        }

        /// <summary>
        /// The gear tier a raid would get right now, for callers that have no RaidPlan of their
        /// own (derelict crews, gifted beings). A preview plan, so a pending calm-before-storm
        /// multiplier is never consumed by it.
        /// </summary>
        public static int CurrentGearTier(GameState S, FactionDef fac) {
            var raid = new CombatRaid(S, 1) { Faction = fac };
            return raid.ComputePlan(preview: true).GearTier;
        }
    }
}
