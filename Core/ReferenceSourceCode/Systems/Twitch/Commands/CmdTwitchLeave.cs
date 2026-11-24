using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Constants;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchLeave : TwitchCommand {
        private const string cmd = "leave";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 0,
                (ctx) => new CmdTwitchLeave(ctx),
                    "Leaves the queue. Won't work if you're already in-game.");
        }

        public CmdTwitchLeave(TwitchCtx ctx) : base(cmd, ctx) {}

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!ctx.TryConsumeRep()) {
                return;
            }
            var q = ctx.Sys.Queue;
            if (q == null || !q.TryRemove(ctx.Viewer)) {
                ctx.Reply("@{0} was not in the queue!", ViewerName);
            } else {
                ctx.Sys.UI.RebuildViewers();
                ctx.Reply("@{0} left the queue", ViewerName);
            }
        }
    }
}
