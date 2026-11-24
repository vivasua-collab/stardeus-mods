using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using KL.Utils;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchQueue : TwitchCommand {
        private const string cmd = "queue";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 1,
                (ctx) => new CmdTwitchQueue(ctx),
                    "Shows the queue size and your position");
        }

        public CmdTwitchQueue(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!ctx.TryConsumeRep()) {
                return;
            }
            var viewer = ctx.Viewer;
            bool isInGame = false;
            var existingBeing = Being;
            if (existingBeing != null) {
                isInGame = true;
            }

            var q = S.Sys.Twitch.Queue;

            if (isInGame) {
                ctx.Reply("you are already in game. {0} people in queue.",
                    q.Queue.Count);
                return;
            }

            var idx = q.NumberOf(viewer);
            if (idx == -1) {
                ctx.Reply("{0} people in queue. You are not in queue, use {1}",
                    q.Size, CommandName("join"));
                return;
            }
            if (ctx.Sys.Settings.QueueMode == TwitchSettings.ViewerQueueMode.Random) {
                ctx.Reply("{0} people in queue (mode: random)", q.Size);
            } else {
                ctx.Reply("{0} people in queue, {1} people before you", q.Size, idx);
            }
        }
    }
}
