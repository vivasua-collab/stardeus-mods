using System;

namespace Game.Systems.Creatures {
    // How traders deal in a species. A species without a Group is never traded, so this block
    // doubles as the feature's per-species switch. Prices are not configured here: a being is
    // worth its sellable body parts plus Species.BeingValueBase for the frame, and these are
    // only the premiums on top (see BeingTrade).
    [Serializable]
    public sealed class SpeciesTradeConfig {
        /// <summary>
        /// Trade group id. Also picks the group header name (market.group.name.{Group}).
        /// </summary>
        public string Group;
        /// <summary>
        /// Chance that a trader who may stock this species has any for sale
        /// </summary>
        public float ChanceInStock;
        public int MaxInStock;
        /// <summary>
        /// Flat credits a living individual is worth on top of its parts and frame
        /// </summary>
        public float LivingPremium;
        /// <summary>
        /// Dead individuals can be sold as salvage (no premiums). Robots only - organic
        /// corpses are never traded.
        /// </summary>
        public bool SellWreck;
    }
}
