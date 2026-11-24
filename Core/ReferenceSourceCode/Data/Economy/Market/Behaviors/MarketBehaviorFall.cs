using Game.Constants;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public sealed class MarketBehaviorFall : MarketBehavior {
        [SelfInit]
        private static void Register() {
            var b = new MarketBehaviorFall();
            Add(Id, b);
        }
        public new const string Id = "Fall";
        private float? chanceLongTerm;
        private float? chanceShortTerm;
        public override float ChanceLongTerm => chanceLongTerm ??= Tunables.MarketFallChanceLongTerm;
        public override float ChanceShortTerm => chanceShortTerm ??= Tunables.MarketFallChanceShortTerm;
        public override float DurationScale => 1f;
        public override bool IsPositive => false;
        public override string NameT => T.MarketBehaviorFall;

        public override float Adjust(Rng rng, float value, float volatility,
                float trendStage, float trendVolatility,
                float recessionEffect, float luck) {
            var v = SmoothlyClampedVolatility(rng, volatility, -luck);
            var change = rng.RangeSmooth(v * 0.2f, v) * trendVolatility;
            // Change no more than x% per day
            var clampedChange = Maths.Min(Tunables.MarketItemMaxNormalPriceFallPerDay, change);
            return value *= 1f - clampedChange;
        }
    }
}
