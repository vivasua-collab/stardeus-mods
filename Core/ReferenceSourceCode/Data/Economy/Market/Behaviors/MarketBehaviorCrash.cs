using System;
using Game.Constants;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public sealed class MarketBehaviorCrash : MarketBehavior {
        [SelfInit]
        private static void Register() {
            var b = new MarketBehaviorCrash();
            Add(Id, b);
        }
        public new const string Id = "Crash";
        private float? chanceLongTerm;
        private float? chanceShortTerm;
        public override float ChanceLongTerm => chanceLongTerm ??= Tunables.MarketCrashChanceLongTerm;
        public override float ChanceShortTerm => chanceShortTerm ??= Tunables.MarketCrashChanceShortTerm;
        public override float DurationScale => 0.8f;
        public override bool IsPositive => false;
        public override string NameT => T.MarketBehaviorCrash;

        public override float Adjust(Rng rng, float value, float volatility,
                float trendStage, float trendVolatility, float recessionEffect, float luck) {
            var v = SmoothlyClampedVolatility(rng, volatility, -luck);
            var fall = rng.RangeSmooth(v, v * 2f) * trendVolatility;
            // Drop no more than x% per day
            var maxDrop = Tunables.MarketItemMaxExtremePriceFallPerDay;
            var clampedFall = Maths.Min(rng.RangeSmooth(maxDrop * 0.5f, maxDrop), fall);
            return value *= 1f - clampedFall;
        }
    }
}
