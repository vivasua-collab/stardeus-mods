using Game.Constants;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public sealed class MarketBehaviorStill : MarketBehavior {
        [SelfInit]
        private static void Register() {
            var b = new MarketBehaviorStill();
            Add(Id, b);
        }
        public new const string Id = "Still";
        private float? chanceLongTerm;
        private float? chanceShortTerm;
        public override float ChanceLongTerm => chanceLongTerm ??= Tunables.MarketStillChanceLongTerm;
        public override float ChanceShortTerm => chanceShortTerm ??= Tunables.MarketStillChanceShortTerm;
        public override bool IsPositive => true;
        public override float DurationScale => 1f;
        public override string NameT => T.MarketBehaviorStill;

        public override float Adjust(Rng rng, float value, float volatility,
                float trendStage, float trendVolatility, float recessionEffect, float luck) {
            var v = SmoothlyClampedVolatility(rng, volatility, luck);
            return value * rng.RangeSmooth(1f - v, 1f + v);
        }
    }
}
