using Game.Constants;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public sealed class MarketBehaviorSurge : MarketBehavior {
        [SelfInit]
        private static void Register() {
            var b = new MarketBehaviorSurge();
            Add(Id, b);
        }
        public new const string Id = "Surge";
        private float? chanceLongTerm;
        private float? chanceShortTerm;
        public override float ChanceLongTerm => chanceLongTerm ??= Tunables.MarketSurgeChanceLongTerm;
        public override float ChanceShortTerm => chanceShortTerm ??= Tunables.MarketSurgeChanceShortTerm;
        public override float DurationScale => 0.8f;
        public override bool IsPositive => true;
        public override string NameT => T.MarketBehaviorSurge;

        public override float Adjust(Rng rng, float value, float volatility,
                float trendStage, float trendVolatility, float recessionEffect, float luck) {
            var v = SmoothlyClampedVolatility(rng, volatility, luck);
            var rise = rng.RangeSmooth(v * 0.5f, v) * Mathf.Pow(trendVolatility, 1.1f);
            // Surge no more than 15% per day
            var maxRise = Tunables.MarketItemMaxExtremePriceRisePerDay;
            var clampedRise = Maths.Min(rng.RangeSmooth(maxRise * 0.5f, maxRise), rise);
            return value *= 1f + clampedRise;
        }
    }
}
