using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.CodeGen;
using Game.Constants;
using Game.Input;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchJoin : TwitchCommand {
        private const string cmd = "join";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 0, (ctx) => new CmdTwitchJoin(ctx),
                "Enters the viewer into the queue");
        }

        public CmdTwitchJoin(TwitchCtx ctx) : base(cmd, ctx) {}

        protected override void Execute() {
            TwitchViewer viewer = ctx.Viewer;
            if (viewer == null) {
                viewer = ctx.Sys.FindOrCreateViewer(ctx.Msg);
            }
            if (!CheckRole(TwitchRoleIdH.Play, viewer)) {
                return;
            }
            var existingBeing = Being;
            if (existingBeing != null) {
                ctx.Reply("@{0} already in game: {1} named {2}",
                    viewer.Name, existingBeing.Species.Name,
                    existingBeing.Mind.Name.FullNameOrNameless);
                return;
            }

            if (!ctx.CheckRep()) {
                return;
            }
            if (ctx.Sys.Queue.TryAdd(viewer)) {
                S.Sys.Log.AddLine(string.Format("{2}<b>{0}</b> has joined the queue. <b>{1}</b> people waiting.",
                    ViewerName, ctx.Sys.Queue.Size, GlyphMap.Sprite("Social", "Twitch")));
                ctx.TryConsumeRep();
                ctx.Sys.UI.RebuildViewers();
                ctx.Reply("{0} has joined the queue. {1} people waiting.",
                    ViewerName, ctx.Sys.Queue.Size);
            } else {
                ctx.Reply("{0} is already in queue. Use {1} to check position.",
                    ViewerName, CommandName("queue"));
            }
        }
    }
}
