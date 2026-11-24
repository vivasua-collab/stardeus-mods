using Game.Constants;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public sealed class MarketBehaviorRise : MarketBehavior {
        [SelfInit]
        private static void Register() {
            var b = new MarketBehaviorRise();
            Add(Id, b);
        }
        public new const string Id = "Rise";
        private float? chanceLongTerm;
        private float? chanceShortTerm;
        public override float ChanceLongTerm => chanceLongTerm ??= Tunables.MarketRiseChanceLongTerm;
        public override float ChanceShortTerm => chanceShortTerm ??= Tunables.MarketRiseChanceShortTerm;
        public override bool IsPositive => true;
        public override float DurationScale => 1.3f;
        public override string NameT => T.MarketBehaviorRise;

        public override float Adjust(Rng rng, float value, float volatility,
                float trendStage, float trendVolatility, float recessionEffect, float luck) {
            var v = SmoothlyClampedVolatility(rng, volatility, luck);
            var change = rng.RangeSmooth(v * 0.2f, v) * trendVolatility;
            // Change no more than 5% per day
            var maxMove = Tunables.MarketItemMaxNormalPriceRisePerDay;
            var clampedChange = Maths.Min(maxMove, change);
            return value *= 1f + clampedChange;
        }
    }
}
