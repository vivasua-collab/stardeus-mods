using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    /// <summary>
    /// How much one player BeingData counts toward Situation.ThreatWeight, the raid-points
    /// headcount term (CombatRaid.ComputePlan's plan.BeingPoints). Popular colony sim prices each pawn
    /// by what it actually is right now (healthy, downed, in cryptosleep, an animal) instead
    /// of a flat 1 per head; this mirrors that so an injured or stashed colony isn't priced
    /// like a healthy one - the previous behavior meant a raid that mauled the colony was
    /// followed by an equally strong one, since wealth and raw headcount are both unchanged
    /// by injuries.
    ///
    /// Deliberately does NOT read combat capability (weapons, DPS, Species.BaseCombatStrength):
    /// that is CombatRaid.StrengthOf's job for costing raiders, and folding it in here would
    /// reintroduce the arming-the-colony-makes-raids-harder feedback loop the points rework
    /// removed. A pawn who can't fight still counts full in Popular colony sim, and does here too.
    /// </summary>
    public static class RaidThreatWeight {
        /// <summary>Non-combat animals/pets count for less - Popular colony sim's animals are a small,
        /// wealth-independent add-on (0.08 x combat power) next to a colonist.</summary>
        internal const float AnimalWeight = 0.15f;

        /// <summary>Stored (stasis pod), on an expedition, or mind-blank (no AI installed):
        /// present in the colony but not defending it right now. Mirrors Popular colony sim's 0.3x
        /// cryptosleep rate.</summary>
        internal const float InactiveWeight = 0.3f;

        /// <summary>Lerp factor between full weight and health-scaled weight, same as
        /// Popular colony sim's Mathf.Lerp(n, n * SummaryHealthPercent, 0.65f).</summary>
        internal const float HealthLerp = 0.65f;

        public static float Of(BeingData data) {
            if (data.HasStateFlag(BeingState.Dead)) { return 0f; }
            if (data.Body.HasFlag(BodyFlags.Missing)) { return 0f; }
            // Left the ship for a colony: still a player record, but not here to be raided,
            // the same as StrengthOf's NotDefending set. Popular colony sim counts only pawns on
            // the map, too.
            if (data.HasStateFlag(BeingState.InColony)) { return 0f; }
            // A being can be mid-setup with no Mind assigned yet - same guard
            // NetWorthCalculator.SumBeingGearAndParts uses before touching EffectiveFaction.
            if (data.Mind == null) { return 0f; }

            var w = data.Species.IsAnimal ? AnimalWeight : 1f;

            if (data.HasStateFlag(BeingState.Stored) || data.HasStateFlag(BeingState.Expedition)
                    || data.Mind.IsBlank) {
                w *= InactiveWeight;
            }

            var health = data.Body.IsIncapacitated ? 0f : 1f - data.Body.InjuryPercent;
            return Mathf.Lerp(w, w * health, HealthLerp);
        }
    }
}
