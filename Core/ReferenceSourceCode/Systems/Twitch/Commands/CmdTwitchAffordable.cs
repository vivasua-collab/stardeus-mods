using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Constants;
using Game.Utils;
using System.Linq;

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchAffordable : TwitchCommand {
        private const string cmd = "affordable";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 1,
                (ctx) => new CmdTwitchAffordable(ctx),
                "Shows affordable commands");
        }
        public CmdTwitchAffordable(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!ctx.TryConsumeRep()) {
                return;
            }
            var viewer = ctx.Viewer;
            var rep = viewer.Reputation;
            ctx.Reply(string.Format("with rep {0} you can afford: {1}",
                viewer.ReputationText,
                string.Join(", ", TwitchSys.CommandMap
                    .Where(c => Settings.IsCommandEnabled(c.Key)
                        && ctx.Sys.CommandCost(c.Key) <= rep)
                    .OrderBy(c => c.Value)
                    .Select(c => CommandName(c.Value)))));
        }
    }
}
