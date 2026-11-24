using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Trade;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class EntityAtTradePortalQuestReward : QuestReward {
        [SelfInit]
        private static void Register() {
            Registries.QuestRewards.Add(RewardId,
                (s, def, data, seed, l) => new EntityAtTradePortalQuestReward(s, def, data, seed, l));
        }

        public const string RewardId = "EntityAtTradePortal";
        private readonly Def entityDef;
        private readonly int amount;
        public string SignalSummary => $"{entityDef.NameT} x{amount}";

        public EntityAtTradePortalQuestReward(GameState s, QuestRewardDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            var defIdKey = config.GetString(PropertyIdH.DVar_DefId, null);
            var amountKey = config.GetString(PropertyIdH.DVar_Amount, null);
            if (string.IsNullOrWhiteSpace(defIdKey)
                    || string.IsNullOrWhiteSpace(amountKey)) {
                throw new Exception(
                    $"EntityAtTradePortal reward requires def and amount DVars: {def}");
            }
            var defId = data.Data?.GetString(defIdKey, null);
            entityDef = The.Defs.Get(defId);
            amount = data.Data?.GetInt(amountKey, 0) ?? 0;
            if (entityDef == null || amount < 1) {
                throw new Exception(
                    $"EntityAtTradePortal reward has invalid entity {defId} x{amount}: {def}");
            }
            Icon = entityDef.Preview;
            Title = $"{Texts.LinkToCodexNoIcon(entityDef)} <b>x{amount}</b>";
        }

        public override string Apply() {
            int posIdx;
            var tradePortal = TradeUI.FindTradePortal(S);
            if (tradePortal != null) {
                posIdx = tradePortal.Tile.Transform.OutPort;
            } else {
                var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                    c => c.Tile.IsConstructed);
                if (cpu == null) {
                    D.Err("Failed applying entity reward, no Trade Portal or Ship Computer: {0}",
                        this);
                    return null;
                }
                var spawnPoint = EntityUtils.SpawnableAround(cpu.Tile.CenterIdx, 5f, true);
                posIdx = EntityUtils.ToPos(spawnPoint);
                if (Pos.IsOOB(posIdx)) {
                    D.Err("Failed applying entity reward, no spawn point near Ship Computer: {0}",
                        this);
                    return null;
                }
            }

            var spawned = EntityUtils.SpawnDef(entityDef, posIdx, new Rng(seed), amount);
            if (spawned == null) {
                D.Err("Failed spawning entity quest reward: {0}", this);
                return null;
            }
            return T.YouReceivedItem_V1.F($"{entityDef.CodexLink} x{amount}");
        }
    }
}
