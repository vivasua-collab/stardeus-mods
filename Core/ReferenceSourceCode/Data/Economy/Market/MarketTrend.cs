using System;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Data.Economy {
    [MessagePackObject]
    public sealed class MarketTrend {
        private MarketBehavior behavior;
        [IgnoreMember] public MarketBehavior Behavior {
            get {
                if (behavior == null && BehaviorId != null) {
                    behavior = MarketBehaviors.Get(BehaviorId);
                }
                return behavior;
            }
            set {
                BehaviorId = value.Id;
                behavior = value;
            }
        }
        [Key("BehaviorId")] public string BehaviorId;
        [Key("VolatilityMul")] public float VolatilityMultiplier;
        [Key("TotalDays")] public int TotalDays;
        [Key("CurrentDays")] public int CurrentDays;
        [IgnoreMember] public float BehaviorStage => (float) CurrentDays / TotalDays;
        [IgnoreMember] public string NameT => Behavior?.NameT ?? Texts.Grey("-");
        [IgnoreMember] public string GroupNameT;


        // Returns true if trend has to change
        public static bool AdvanceDay(MarketTrend trend) {
            if (trend == null) { return true; }
            return trend.CurrentDays++ >= trend.TotalDays;
        }

        public float Adjust(Rng rng, float value, float volatility,
                float recessionEffect, float luck) {
            if (Behavior == null) {
                D.Err("Behavior is null for market trend: {0}", this);
                return value;
            }
            float newVal = Behavior.Adjust(rng, value, volatility, BehaviorStage,
                VolatilityMultiplier, recessionEffect, luck);
            float r = (newVal - value) / value;
            if (r > 0.15f || r < -0.15f) {
                D.Err("Behavior {0} adjusted {1} by {2}", Behavior.Id, value,
                    Units.PercentageSigned(r));
            }
            return newVal;
        }
    }
}
