using System;
using Game.Constants;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Economy {
    public abstract class MarketBehavior {
        protected static void Add(string id, MarketBehavior instance) {
            instance.Id = id;
            instance.IdH = Hashes.S(id);
            MarketBehaviors.Add(id, instance.IdH, instance);
        }
        public string Id;
        public int IdH;
        public abstract float ChanceLongTerm { get; }
        public abstract float ChanceShortTerm { get; }
        /// <summary>
        /// How long the effect should last relatively to the default range
        /// </summary>
        public abstract float DurationScale { get; }
        public abstract bool IsPositive { get; }
        public virtual bool IgnoreRoot => false;
        public abstract string NameT { get; }
        private float? marketChangeRate;
        private float MarketChangeRate => marketChangeRate ??=
                Tunables.MarketChangeRate;


        /// <param name="value">price that we are adjusting</param>
        /// <param name="valueVolatility"></param>
        /// <param name="behaviorStage">percentage of completion</param>
        /// <param name="trendVolatilityMultiplier"></param>
        /// <param name="recessionEffect">how sensitive to recession this item is</param>
        /// <param name="luck"></param>
        /// <returns></returns>
        public abstract float Adjust(Rng rng, float value, float valueVolatility,
            float behaviorStage, float trendVolatilityMultiplier,
            float recessionEffect, float luck);

        public int AdjustDuration(int days, int minDays) {
            days = Mathf.CeilToInt(days * DurationScale / MarketChangeRate);
            if (days < minDays) {
                return minDays;
            }
            return days;
        }

        protected float SmoothlyClampedVolatility(Rng rng, float volatility, float luck) {
            var v = volatility + luck;
            if (v < 0.001f) {
                v = 0.001f;
            } else if (v > 0.01f) {
                v = rng.RangeSmooth(0.01f, volatility);
            }
            return v;
        }
    }
}
