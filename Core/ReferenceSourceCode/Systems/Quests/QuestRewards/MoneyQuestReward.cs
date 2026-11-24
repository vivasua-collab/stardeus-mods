using System;
using Game.CodeGen;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class MoneyQuestReward : BaseMoneyQuestReward, IRewardPackage {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new MoneyQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "Money";
        public MoneyQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            SetAmount(def);
        }

        private void SetAmount(QuestRewardDef def) {
            // TODO add support for dynamic net worth fraction and save / load
            var cfg = def.Config;
            var amt = cfg.GetInt(PropertyIdH.Amount, 0);
            if (amt == 0) {
                throw new Exception($"Money reward has 0 Amount: {def}");
            }
            UpdateRewardInfo(amt);
            amount = amt;
        }

        public bool PopulatePackage(QuestPackage package) {
            package.Money = amount;
            return true;
        }
    }
}
