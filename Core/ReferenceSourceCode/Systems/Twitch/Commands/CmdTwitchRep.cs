using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Constants;
using Game.Utils;

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchRep : TwitchCommand {
        private const string cmd = "rep";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 1,
                (ctx) => new CmdTwitchRep(ctx),
                "Shows your reputation");
        }
        public CmdTwitchRep(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!ctx.TryConsumeRep()) {
                return;
            }
            var name = ctx.Msg.Args;
            if (name == "gain") {
                ctx.Reply("Rep gain: +{0}/h", ctx.Sys.Settings.RepGainPerHour);
                return;
            }

            bool self;
            if (string.IsNullOrWhiteSpace(name)) {
                name = ctx.Msg.Username;
                self = true;
            } else {
                self = false;
            }
            var viewer = ctx.Sys.FindViewer(name);
            if (viewer == null) {
                if (!self) {
                    ctx.Reply("no viewer with username: {0}", name);
                }
                return;
            }
            ctx.Reply("{0} -> Rep: {1}", name, viewer.ReputationText);
        }
    }
}
