using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using System.Text;
using Game.Input;
using KL.Grid;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchWhere : TwitchCommand {
        private const string cmd = "where";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 1,
                (ctx) => new CmdTwitchWhere(ctx),
                    "Gives coordinates of the mouse cursor and your being");
        }
        public CmdTwitchWhere(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!ctx.TryConsumeRep()) {
                return;
            }
            var mousePos = EntityUtils.ToPosSafe(The.Bindings.Mouse.WorldPosition);
            var being = Being;
            int beingPos = Pos.Invalid;
            if (being != null && being.Being != null) {
                beingPos = being.Being.PosIdx;
            }
            if (beingPos != Pos.Invalid) {
                ctx.Reply("Mouse at {0}. {1} at {2}",
                    Pos.String(mousePos), being.Name, Pos.String(beingPos));
            } else if (being != null) {
                ctx.Reply("Mouse at {0}. {1} position not available",
                    Pos.String(mousePos), being.Name);
            } else {
                ctx.Reply("Mouse at {0}", Pos.String(mousePos));
            }
        }
    }
}
