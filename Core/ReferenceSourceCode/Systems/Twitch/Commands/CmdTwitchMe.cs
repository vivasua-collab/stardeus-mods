using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using System.Text;
using Game.Input;
using KL.Grid;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchMe : TwitchCommand {
        private const string cmd = "me";
        private static StringBuilder sb;

        [SelfInit]
        protected static void Register() {
            sb = null;
            TwitchSys.RegisterCommand(cmd, 1,
                (ctx) => new CmdTwitchMe(ctx),
                "Shows information about the viewer");
        }
        public CmdTwitchMe(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!ctx.TryConsumeRep()) {
                return;
            }
            var being = Being;
            sb ??= new();
            sb.Clear();
            if (ViewerName != ctx.Viewer?.Name) {
                sb.Append('@');
                sb.Append(ViewerName);
            }
            sb.Append(" -> ");
            if (being != null) {
                sb.AppendFormat("Controlling a {0} named {1}, ",
                    being.Species.Name, being.Mind.Name.FullNameOrNameless);
                if (being.Being != null) {
                    sb.AppendFormat("Location: {0} ", Pos.String(being.Being.PosIdx));
                }
            } else {
                var qNumber = ctx.Sys.Queue?.NumberOf(ctx.Viewer) ?? -1;
                if (qNumber != -1) {
                    if (ctx.Sys.Settings.QueueMode == TwitchSettings.ViewerQueueMode.Queue) {
                        sb.AppendFormat("In Queue (#{0}), ", qNumber);
                    } else {
                        sb.Append("In Queue, ");
                    }
                }
            }
            ctx.Viewer.AppendInfo(sb);
            ctx.Reply(sb.ToString());
            sb.Clear();
        }
    }
}
