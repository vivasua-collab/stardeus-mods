using System;
using Game.CodeGen;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class MarketItemQuestVar : QuestVar, IQuestVar<Def> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new MarketItemQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "MarketItem";
        private Def value;
        private float unitValue;
        public Def Value => value;
        public int Amount { get; private set; }
        public float Money { get; private set; }
        public float TotalValue => unitValue * Amount;

        public MarketItemQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            var defIdKey = RequiredDVarKey(PropertyIdH.DVar_DefId);
            var amountKey = RequiredDVarKey(PropertyIdH.DVar_Amount);
            var moneyKey = config.GetString(PropertyIdH.DVar_Money, null);
            if (moneyKey != null && string.IsNullOrWhiteSpace(moneyKey)) {
                throw new Exception($"MarketItem has an invalid DVar_Money key: {Def}");
            }
            if (isLoading) {
                LoadPersisted(defIdKey, amountKey, moneyKey);
                return;
            }

            var rng = new Rng(Hashes.Quick(seed, def.IdH));
            var excluded = ResolveReferencedVar(PropertyIdH.Var_Exclude)?.Value;
            value = PickDef(rng, excluded);
            unitValue = MarketValue(value);
            var percent = ResolvePercent(rng);
            if (!float.IsFinite(percent) || percent <= 0f) {
                throw new Exception($"MarketItem produced invalid percent {percent}: {Def}");
            }
            var matched = ResolveReferencedVar(PropertyIdH.Var_MatchValueOf);
            float dealValue;
            if (matched != null) {
                dealValue = matched.TotalValue * percent;
                if (!float.IsFinite(dealValue) || dealValue <= 0f) {
                    throw new Exception($"MarketItem produced invalid matched value {dealValue}: {Def}");
                }
                Amount = Mathf.Max(1, Mathf.CeilToInt(dealValue / unitValue));
            } else {
                Amount = ResolveQuantity(rng);
                dealValue = unitValue * Amount * percent;
            }
            if (!float.IsFinite(dealValue) || dealValue <= 0f) {
                throw new Exception($"MarketItem produced invalid deal value {dealValue}: {Def}");
            }

            if (moneyKey != null) {
                Money = Maths.ToNegotiationValue(dealValue);
                if (!float.IsFinite(Money) || Money <= 0f) {
                    throw new Exception($"MarketItem produced invalid money value: {this}");
                }
            }

            var vars = data.GetOrCreateData();
            vars.SetString(defIdKey, value.Id);
            vars.SetInt(amountKey, Amount);
            if (moneyKey != null) {
                vars.SetFloat(moneyKey, Money);
            }
        }

        private string RequiredDVarKey(int propertyIdH) {
            var key = config.GetString(propertyIdH, null);
            if (string.IsNullOrWhiteSpace(key)) {
                throw new Exception(
                    $"MarketItem requires {PropertyIdH.ResolveName(propertyIdH)}: {Def}");
            }
            return key;
        }

        private void LoadPersisted(string defIdKey, string amountKey, string moneyKey) {
            if (data.Data == null) {
                throw new Exception($"MarketItem has no persisted quest data: {Def} | {data}");
            }
            var defId = data.Data.GetString(defIdKey, null);
            value = The.Defs.Get(defId);
            if (value == null) {
                throw new Exception($"MarketItem failed restoring def {defId}: {Def} | {data}");
            }
            Amount = data.Data.GetInt(amountKey, 0);
            if (Amount < 1) {
                throw new Exception($"MarketItem failed restoring amount {Amount}: {Def} | {data}");
            }
            unitValue = MarketValue(value);
            if (moneyKey != null) {
                Money = data.Data.GetFloat(moneyKey, 0f);
                if (!float.IsFinite(Money) || Money <= 0f) {
                    throw new Exception($"MarketItem failed restoring money {Money}: {Def} | {data}");
                }
            }
        }

        private MarketItemQuestVar ResolveReferencedVar(int propertyIdH) {
            var varId = config.GetString(propertyIdH, null);
            if (varId == null) {
                return null;
            }
            var variable = data.GetQuestVar(varId) as MarketItemQuestVar;
            if (variable == null) {
                throw new Exception(
                    $"MarketItem reference {varId} is not a prior MarketItem var: {Def} | {data}");
            }
            return variable;
        }

        private Def PickDef(Rng rng, Def excluded) {
            Caches.DefSet.Clear();
            var defIds = config.GetStringSet(PropertyIdH.DefIds, null);
            if (!Arrays.IsEmpty(defIds)) {
                for (int i = 0; i < defIds.Length; i++) {
                    var candidate = The.Defs.Get(defIds[i]);
                    if (candidate == null) {
                        throw new Exception($"MarketItem contains invalid DefId {defIds[i]}: {Def}");
                    }
                    Caches.DefSet.Add(candidate);
                }
            }

            var marketGroups = config.GetStringSet(PropertyIdH.MarketGroups, null);
            if (!Arrays.IsEmpty(marketGroups)) {
                for (int i = 0; i < marketGroups.Length; i++) {
                    if (MarketGroup.Find(marketGroups[i]) == null) {
                        throw new Exception(
                            $"MarketItem contains invalid MarketGroup {marketGroups[i]}: {Def}");
                    }
                }
                // Offers follow the trader rules: gated by the provider's sector, weighted by
                // rarity, never a NoRandomLoot story item
                var sectorDiff = SOUtils.SectorDifficultyOf(data.Provider?.ProviderSO,
                    A.PlayerSector);
                if (MarketLootPool.TryPick(S, marketGroups, sectorDiff, 0f, excluded, rng,
                        out var picked)) {
                    Caches.DefSet.Add(picked.Def);
                }
            }

            if (excluded != null) {
                Caches.DefSet.Remove(excluded);
            }
            if (Caches.DefSet.Count == 0) {
                throw new Exception($"MarketItem has no candidate defs after filtering: {Def}");
            }
            return rng.From(Caches.DefSet);
        }

        private float MarketValue(Def itemDef) {
            var marketItem = S.Market.Find(itemDef.IdH);
            if (marketItem == null) {
                throw new Exception($"Market item not found for def {itemDef}: {Def}");
            }
            var value = marketItem.Adjusted().Value;
            if (!float.IsFinite(value) || value <= 0f) {
                throw new Exception($"Market item has invalid value {value}: {itemDef} | {Def}");
            }
            return value;
        }

        private int ResolveQuantity(Rng rng) {
            var from = config.GetInt(PropertyIdH.QuantityFrom, 0);
            var to = config.GetInt(PropertyIdH.QuantityTo, 0);
            var amount = from > 0 && to >= from
                ? rng.Range(from, to)
                : config.GetInt(PropertyIdH.Quantity, 0);
            return Mathf.Max(1, amount);
        }

        private float ResolvePercent(Rng rng) {
            var to = config.GetFloat(PropertyIdH.PercentTo, 0f);
            if (to <= 0.00001f) {
                return 1f;
            }
            var from = config.GetFloat(PropertyIdH.PercentFrom, 0f);
            return rng.Range(from, to);
        }
    }
}
