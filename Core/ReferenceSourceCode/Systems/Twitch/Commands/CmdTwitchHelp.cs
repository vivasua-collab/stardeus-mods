using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using System.Linq;
using KL.Utils;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchHelp : TwitchCommand {
        private const string cmd = "help";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 0,
                (ctx) => new CmdTwitchHelp(ctx),
                    "Lists all commands or shows help if another command name is given");
        }

        private CmdTwitchHelp(TwitchCtx ctx) : base(cmd,  ctx) {}

        protected override void Execute() {
            if (ctx.TryConsumeRep()) {
                ShowHelp();
            }
        }
        public void ShowHelp() {
            // D.Warn("Help from msg: whisper: {0}, channel: {1}, user: {2}", msg.IsWhisper, msg.IsChannelMessage, msg.Username);
            var args = Args;
            if (string.IsNullOrWhiteSpace(args)) {
                ctx.Reply(string.Format("/me Stardeus commands: {0}",
                    string.Join(" • ", TwitchSys.CommandMap
                        .Where(c => Settings.IsCommandEnabled(c.Key))
                        .OrderBy(c => c.Value)
                        .Select(c => CommandName(c.Value)))));
                return;
            }
            if (!args.StartsWith("!")) {
                args = $"!{args}";
            }
            args = Settings.StripPrefix(args);
            var cmdH = Hashes.S(args);
            if (TwitchSys.HelpLines.TryGetValue(cmdH, out var line)) {
                ctx.Reply($"/me {args}: {line}");
            } else {
                ctx.Reply($"/me {args}: command not found");
            }
        }
    }
}
