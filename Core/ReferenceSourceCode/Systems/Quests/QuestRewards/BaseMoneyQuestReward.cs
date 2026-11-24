using System;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Utils;

namespace Game.Systems.Quests {
    public abstract class BaseMoneyQuestReward : QuestReward {
        protected float amount;
        protected BaseMoneyQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
            : base(s, def, data, seed, isLoading) {
        }

        public override string Tooltip =>
            $"{Desc}<br>{T.OurWallet}: {Texts.Green(S.Wallet.ValueString)}";


        protected void UpdateRewardInfo(float amt) {
            Title = Wallet.ToValueString(amt);
            if (amt > 0) {
                Title = Texts.Green(Title);
            } else {
                Title = $"{Texts.RedMinus}{Texts.Red(Title)}";
            }
            Icon = IconId.CStarcreds;
            if (amt > 0) {
                Desc = $"{T.YouWillReceive}: {Title}";
            } else {
                Desc = $"{T.YouWillPay}: {Title}";
            }
        }

        public override string Apply() {
            if (amount > 0) {
                S.Wallet.Add(amount);
                return T.YouReceivedItem_V1.F(Texts.Green(Wallet.ToValueString(amount)));
            }
            S.Wallet.Add(-Math.Min(S.Wallet.Amount, amount));
            return T.YouLostItem_V1.F(Texts.Red(Wallet.ToValueString(amount)));
        }
    }
}
