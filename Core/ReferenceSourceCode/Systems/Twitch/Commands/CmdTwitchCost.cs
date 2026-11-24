using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using KL.Utils;
using System.Linq;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchCost : TwitchCommand {
        private const string cmd = "cost";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 0,
                (ctx) => new CmdTwitchCost(ctx),
                    "Shows cost of a command");
        }

        private CmdTwitchCost(TwitchCtx ctx) : base(cmd,  ctx) {}

        protected override void Execute() {
            if (ctx.TryConsumeRep()) {
                ShowCost(ctx.Msg);
            }
        }

        public void ShowCost(TwitchMessage msg) {
            var args = msg.Args;

            if (string.IsNullOrWhiteSpace(args)) {
                ctx.Reply("usage: {0} <command>", CommandName("cost"));
                return;
            }

            if (!args.StartsWith("!")) {
                args = $"!{args}";
            }
            var cmd = ctx.Sys.Settings.StripPrefix(args);
            var cmdH = Hashes.S(cmd);

            if (!TwitchSys.CommandMap.ContainsKey(cmdH)) {
                ctx.Reply("unknown command: {0}", args);
                return;
            }

            var cost = ctx.Sys.CommandCost(cmdH);
            ctx.Reply("{0} cost: {1}", CommandName(cmd), cost);
        }
    }
}
