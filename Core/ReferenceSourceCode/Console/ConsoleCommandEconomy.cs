using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using System.Text;
using Game.Data.Economy;
using KL.Randomness;
using System.Linq;
using System.Collections;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.Data.Factions;
using Game.Systems.AI.Combat;
using Game.Systems.Economy;

namespace Game.Console {
    public class ConsoleCommandEconomy : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandEconomy());
        }
        private Wallet wallet => The.R.State.Wallet;
        protected override Dictionary<string, string> subCommands => new() {
            // economy wallet_add Starcreds 10000
            { "wallet_add", "WalletAdd" },
            { "wallet_take", "WalletTake" },
            { "wallet", "WalletShow" },
            { "start_trade", "StartTrade" },
            { "reset_stock_market", "ResetStockMarket" },
            { "networth", "NetWorth" },
        };

        protected ConsoleCommandResult NetWorth(ConsoleCommandArguments args) {
            var S = The.R.State;
            var econ = S.Sys.Economy;
            econ.UpdateNetWorth();
            var b = econ.WealthBreakdown;
            var sb = new StringBuilder();
            sb.Append("Wealth (base prices):");
            RaidPlan.DescribeWealth(sb, b);
            sb.AppendFormat("\nLiquid subtotal (pre-widening BaseNetWorth): {0}",
                Wallet.ToValueString(b.Liquid));
            sb.AppendFormat("\nBaseNetWorth (used by story pacing): {0} ({1:0.###}M)",
                Wallet.ToValueString(b.Total), S.Situation.BaseNetWorth);
            sb.AppendFormat("\nThreat wealth (raid strength/cadence, structures at {0:0.##}x): {1} ({2:0.###}M)",
                NetWorthCalculator.Breakdown.StructureThreatWeight,
                Wallet.ToValueString(b.ThreatTotal), CombatRaid.ThreatNetWorth(S.Situation, b));
            sb.AppendFormat("\nCombined/liquid ratio: {0:0.##}x",
                b.Liquid > 0d ? b.Total / b.Liquid : 0d);
            sb.AppendFormat("\nNet worth after market adjustment: {0}",
                Wallet.ToValueString(econ.NetWorthTotal));
            #if !PRODUCTION_BUILD
            sb.AppendFormat("\nSweep: {0} components visited in {1:0.###}ms",
                b.ComponentsVisited, econ.LastWealthSweepMs);
            #endif
            return OK(sb.ToString());
        }

        protected ConsoleCommandResult StartTrade(ConsoleCommandArguments args) {
            var S = The.R.State;
            int seed = 0;
            if (args.HasArgument(2)) { seed = args.GetInt(2); }
            if (seed == 0) {
                seed = Rng.UInt;
            }
            var trade = S.Query.CreateRandomTrade.Ask(new Rng(seed), FactionDef.Get("Capitalist"));
            return Close();
        }

        protected ConsoleCommandResult ResetStockMarket(ConsoleCommandArguments args) {
            Coroutines.Start(ResimulateStockMarket(), this, "ResimulateStockMarket", false, false);
            return OK("Resimulating stock market. A popup will appear when it's done.");
        }

        private IEnumerator ResimulateStockMarket() {
            var s = The.R.State;
            var start = GameTimer.realtimeSinceStartupAsDouble;
            s.Market.Reset();
            for (long tick = 0; tick < G.Ticks; tick += Consts.TicksPerDay) {
                s.Market.Tick(tick);
                if (!GameTimer.HasAvailableFrameBudget()) {
                    yield return null;
                    if (!GameState.IsCurrent(s)) { yield break; }
                }
            }
            var end = GameTimer.realtimeSinceStartupAsDouble;
            var took = end - start;
            var days = G.Ticks / Consts.TicksPerDay;
            WPopup.Create(false, IconId.CStarcreds, T.Success, $"{days} days of stock market have been resimulated in {took} seconds").Spawn();
        }

        protected ConsoleCommandResult WalletShow(ConsoleCommandArguments args) {
            var sb = new StringBuilder();
            sb.AppendFormat("Wallet contents: {0}\n", The.R.State.Wallet.ValueString);
            return OK(sb.ToString());
        }

        protected ConsoleCommandResult WalletAdd(ConsoleCommandArguments args) {
            var amt = args.GetInt(2);
            The.R.State.Wallet.Add(amt);
            return OK("Balance now: {0}", The.R.State.Wallet.ValueString);
        }
        protected ConsoleCommandResult WalletTake(ConsoleCommandArguments args) {
            var amt = args.GetInt(2);
            if (wallet.TryPay(amt, null)) {
                return OK("Balance now: {0}", wallet.ValueString);

            } else {
                return Error("Couldn't take. Balance now: {0}", wallet.ValueString);
            }
        }

        public override void Initialize() {
            Name = "economy";
            HelpLine = "Sets need value to given one for all beings that have that need";
        }
    }
}
