using System;
using Game.Constants;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public sealed class MarketBehaviorRecession : MarketBehavior {
        [SelfInit]
        private static void Register() {
            var b = new MarketBehaviorRecession();
            Add(Id, b);
        }
        public int daysInRecession;
        public new const string Id = "Recession";
        private float? chanceLongTerm;
        private float? chanceShortTerm;
        private float? strength;
        public float Strength => strength ??= Tunables.MarketRecessionStrength;
        public override float ChanceLongTerm => chanceLongTerm ??= Tunables.MarketRecessionChanceLongTerm;
        public override float ChanceShortTerm => chanceShortTerm ??= Tunables.MarketRecessionChanceShortTerm;
        public override float DurationScale => 2.0f;
        public override bool IsPositive => false;
        public override bool IgnoreRoot => true;
        public override string NameT => T.MarketBehaviorRecession;
        private readonly Curve Curve = new float[] {
            0f, 0.25f,
            0.05f, 0.2f,
            0.1f, 0.15f,
            0.15f, 0.10f,
            0.2f, 0.05f,
            0.3f, 0.10f,
            0.4f, 0.15f,
            0.5f, 0.2f,
            0.6f, 0.15f,
            0.7f, 0.07f,
            0.8f, 0.05f,
            0.9f, 0.01f,
            1f, 0.1f,
        };

        public override float Adjust(Rng rng, float value, float volatility,
                float trendStage, float trendVolatilityMul, float recessionEffect,
                float luck) {
            var val = Curve.Evaluate(trendStage);
            var v = SmoothlyClampedVolatility(rng, volatility, -luck);
            var recessionImpact = v + Strength;
            var wantDrop = recessionEffect * val * recessionImpact;
            // Drop no more than x% per day
            var maxDrop = rng.RangeSmooth(
                Tunables.MarketItemMaxNormalPriceFallPerDay,
                Tunables.MarketItemMaxExtremePriceFallPerDay);
            var clampedDrop = Math.Min(maxDrop, wantDrop);
            var newValue = value * (1f - clampedDrop);
            // D.Log("Recession: Val {0:0.0000}->{1:0.0000}, Vol:{2}, TVol:{3}, S:{4}, RecEff:{5}, RecImp:{6} WandDrop:{7:0.0000}, CDrop:{8:0.0000}. D:{9}",
            //     value, newValue, volatility, trendVolatilityMul, trendStage,
            //     recessionEffect, recessionImpact, wantDrop, clampedDrop, ++daysInRecession);
            return newValue;
        }
    }
}
