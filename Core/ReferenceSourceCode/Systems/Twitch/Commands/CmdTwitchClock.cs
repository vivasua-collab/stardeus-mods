using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Constants;
using Game.Utils;
using Game.UI;

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchClock : TwitchCommand {
        private const string cmd = "clock";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 60,
                (ctx) => new CmdTwitchClock(ctx),
                "Controls the game clock");
        }
        public CmdTwitchClock(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            var args = Args;
            if (args != null) {
                args = args.ToLowerInvariant();
            }
            switch (args) {
                case "pause" :
                case "stop" : {
                    if (S.Clock.IsStopped) {
                        ctx.Reply("clock is already stopped");
                        return;
                    }
                    if (!ctx.TryConsumeRep()) {
                        return;
                    }
                    S.Clock.SetSpeed(ClockSpeed.Stopped);
                    UISounds.PlayClockChange(S.Clock.Speed);
                    ctx.Reply("{0} stopped the clock", ViewerName);
                    break;
                }
                case "unpause" :
                case "resume" :
                case "go" : {
                    if (!S.Clock.IsStopped) {
                        ctx.Reply("clock is not stopped");
                        return;
                    }
                    if (!ctx.TryConsumeRep()) { return; }
                    S.Clock.SetSpeed(ClockSpeed.Normal);
                    UISounds.PlayClockChange(S.Clock.Speed);
                    ctx.Reply("{0} resumed the clock", ViewerName);
                    break;
                }
                default: {
                    ctx.Reply("usage: {0} <stop|go>", CommandName(cmd));
                    return;
                }
            }
        }
    }
}
