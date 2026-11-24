using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class Money_ResourceMarketValueMulQuestReward : BaseMoneyQuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new Money_ResourceMarketValueMulQuestReward(
                    s, def, data, seed, l));
        }

        public const string RewardId = "Money_ResourceMarketValueMul";
        public Money_ResourceMarketValueMulQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            if (isLoading) {
                if (data.Data.TryGetFloat(def.DataKey(RewardId), out amount)) {
                    UpdateRewardInfo(amount);
                    return;
                }
                D.Err("Failed to load reward amount for reward {0} | {1}", def, data);
            }

            var matTypeIdKey = def.Config.GetString(PropertyIdH.DVar_MatTypeId);
            var matType = data.Data.GetString(matTypeIdKey);

            var amtKey = def.Config.GetString(PropertyIdH.DVar_Amount);
            var amt = data.Data.GetInt(amtKey);
            var mul = GetQuestVarByKey(PropertyIdH.Var_Multiplier, isRequired: false)
                ?.GetValue<float>() ?? 1f;

            var mt = MatType.Get(matType);
            if (mt == null) {
                throw new Exception($"MatType not found by id {matType} | {def} | {data}");
            }

            var val = S.Sys.Economy.WorthOf(mt, amt, true);
            val *= mul;
            if (val > float.MaxValue) {
                amount = float.MaxValue;
            } else {
                amount = (float) val;
            }
            if (amount > 100000) {
                var k10 = ((int) amount) / 10000;
                amount = k10 * 10000;
            } else if (amount > 1000) {
                var k = ((int) amount) / 1000;
                amount = k * 1000;
            }
            var d = data.GetOrCreateData();
            d.SetFloat(def.DataKey(RewardId), amount);
            UpdateRewardInfo(amount);
        }
    }
}
