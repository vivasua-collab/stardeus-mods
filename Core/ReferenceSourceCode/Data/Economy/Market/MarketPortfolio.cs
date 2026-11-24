using System;
using System.Collections.Generic;
using Game.Achievements;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data.Economy {
    [MessagePackObject]
    public sealed class MarketPortfolio {
        [Key("Positions")] public List<MarketPosition> Positions;
        [Key("History")] public Dictionary<int, float> History;
        public void Add(MarketPosition pos) {
            if (Positions == null) {
                Positions = new();
            }
            Positions.Add(pos);
        }

        public List<MarketPosition> PositionsFor(string id) {
            if (Positions == null) { return null; }
            List<MarketPosition> positions = null;
            for (int i = 0; i < Positions.Count; i++) {
                MarketPosition p = Positions[i];
                if (p.Id == id) {
                    if (positions == null) {
                        positions = new();
                    }
                    positions.Add(p);
                }
            }
            return positions;
        }

        public int OwnedPositions(int id) {
            if (Positions == null) { return 0; }
            int pos = 0;
            for (int i = 0; i < Positions.Count; i++) {
                MarketPosition p = Positions[i];
                if (p.IdH == id) {
                    if (pos + p.Position > int.MaxValue) {
                        D.Err("Owning more than int.MaxValue! {0}", id);
                        pos = int.MaxValue;
                        break;
                    }
                    pos += p.Position;
                }
            }
            return pos;
        }
        public int OwnedPositions(int id, out float costBasis, out float avgPrice) {
            costBasis = avgPrice = 0;
            if (Positions == null) { return 0; }
            int pos = 0;
            for (int i = 0; i < Positions.Count; i++) {
                MarketPosition p = Positions[i];
                if (p.IdH == id) {
                    if (pos + p.Position > int.MaxValue) {
                        D.Err("Owning more than int.MaxValue! {0}", id);
                        pos = int.MaxValue;
                        break;
                    }
                    pos += p.Position;
                    costBasis += p.Position * p.Cost;
                }
            }
            avgPrice = costBasis / pos;
            return pos;
        }
        public void Buy(MarketItemModifier mod, int amount) {
            var pos = new MarketPosition {
                Id = mod.Id,
                IdH = mod.IdH,
                Position = amount,
                Cost = mod.Price,
                Tick = G.Ticks,
            };

            if (mod.S.Wallet.TryPay(amount * mod.Price, null)) {
                Add(pos);
            }
        }

        public void Sell(MarketItemModifier mod, int amount) {
            if (Positions == null) { return; }
            var totalSold = 0;
            var baseCost = 0f;
            for (int i = Positions.Count - 1; i >= 0; i--) {
                MarketPosition p = Positions[i];
                if (p.IdH == mod.IdH) {
                    var sold = Mathf.Min(amount, p.Position);
                    totalSold += sold;
                    baseCost += p.Cost * sold;
                    if (sold < p.Position) {
                        p.Position -= sold;
                    } else {
                        // Sold out
                        Positions.RemoveAt(i);
                    }
                    if (totalSold == amount) { break; }
                }
            }
            var income = totalSold * mod.Price;
            var profit = income - baseCost;
            if (profit <= -100_000) {
                The.AchievementTracker.Unlock(
                    AchievementStockMarketLoss.AchievementId);
            }
            mod.S.Wallet.Add(income);
        }

        public void AfterLoad() {
            if (Positions == null) { return; }
            for (int i = Positions.Count - 1; i >= 0; i--) {
                MarketPosition p = Positions[i];
                p.IdH = Hashes.S(p.Id);
            }
        }
    }
}
