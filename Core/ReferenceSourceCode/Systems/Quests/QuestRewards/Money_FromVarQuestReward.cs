using System;
using Game.CodeGen;
using Game.Data;
using Game.Data.Economy;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class Money_FromVarQuestReward : BaseMoneyQuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new Money_FromVarQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "Money_FromVar";
        public string SignalSummary => Wallet.ToValueString(amount);

        public Money_FromVarQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            var moneyKey = config.GetString(PropertyIdH.DVar_Money, null);
            if (string.IsNullOrWhiteSpace(moneyKey)) {
                throw new Exception($"Money_FromVar reward requires DVar_Money: {def}");
            }
            amount = data.Data?.GetFloat(moneyKey, 0f) ?? 0f;
            if (!float.IsFinite(amount) || amount <= 0f) {
                throw new Exception($"Money_FromVar reward has invalid amount {amount}: {def}");
            }
            UpdateRewardInfo(amount);
        }
    }
}
