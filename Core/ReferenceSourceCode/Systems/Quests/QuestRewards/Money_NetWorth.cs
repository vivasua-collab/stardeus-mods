using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class Money_NetWorthQuestReward : BaseMoneyQuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new Money_NetWorthQuestReward(
                    s, def, data, seed, l));
        }

        public const string RewardId = "Money_NetWorth";
        public Money_NetWorthQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            if (isLoading) {
                if (data.Data.TryGetFloat(def.DataKey(RewardId), out amount)) {
                    UpdateRewardInfo(amount);
                    return;
                }
                D.Err("Failed to load reward amount for reward {0} | {1}", def, data);
            }

            var rng = new Rng(seed);
            var netWorthFrac = rng.Curve(def.Config.GetFloatSet(PropertyIdH.NetWorthCurve));
            var min = def.Config.GetInt(PropertyIdH.AmountMin, 0);
            var max = def.Config.GetInt(PropertyIdH.AmountMax, 0);
            S.Sys.Economy.UpdateNetWorth();
            double nw = S.Situation.NetWorth * 1_000_000d;
            double amt = (float) (nw * netWorthFrac);
            if (amt > float.MaxValue) {
                amt = float.MaxValue;
            }
            if (min > 0 && amt < min) {
                amt = min;
            }
            if (max > 0 && amt > max) {
                amt = max;
            }
            amt *= SectorDifficultyMul();
            if (amt > 100000d) {
                amount = (int) (amt / 10000d) * 10000f;
            } else if (amt > 10000d) {
                amount = (int) (amt / 1000d) * 1000f;
            } else {
                amount = (int) (amt / 100d) * 100f;
            }
            var d = data.GetOrCreateData();
            d.SetFloat(def.DataKey(RewardId), amount);
            UpdateRewardInfo(amount);
        }
    }
}
