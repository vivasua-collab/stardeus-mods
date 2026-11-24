using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using System.Text;
using Game.Input;
using KL.Grid;
using Game.Constants;
using Game.CodeGen;
using KL.Utils;
using Game.UI;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchCam : TwitchCommand {
        private const string cmd = "cam";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 30,
                (ctx) => new CmdTwitchCam(ctx),
                    "Controls the Camera");
        }
        public CmdTwitchCam(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) { return; }
            if (!CheckRole(TwitchRoleIdH.Camera)) { return; }
            if (UIShowing.FullScreenWidget || UIShowing.ScreenBlock) {
                ctx.Reply("cannot control the camera right now");
                return;
            }
            if (!ctx.CheckRep()) { return; }
            var args = Args;
            string showArg = null;
            if (args != null) {
                args = args.ToLowerInvariant();
                if (args.StartsWith("show ")) {
                    showArg = Strings.CutPart(args, " ", null, false).Trim();
                    args = "show";
                }
            }
            switch (args) {
                case "me" : {
                    if (!TryGetBeing(out var being,
                            skipIncapacitated: false,
                            skipUnderDC: false,
                            skipNonInterruptable: false)) {
                        return;
                    }
                    ctx.TryConsumeRep();
                    S.Sig.FollowEntity.Send(being.Being);
                    ctx.Reply("camera is following {0}", ViewerName);
                    break;
                }
                case "ship" : {
                    ctx.TryConsumeRep();
                    EntityUtils.CameraFocusOnShip();
                    ctx.Reply("{0} has requested the ship view", ViewerName);
                    break;
                }
                case "show" : {
                    Show(showArg);
                    break;
                }
                default: {
                    ctx.Reply("usage: {0} <me|ship|show [pos or object name]>", CommandName(cmd));
                    return;
                }
            }
        }

        private void Show(string args) {
            if (Pos.TryParse(args, out var coord)) {
                EntityUtils.CameraFocusOn(coord, false);
                ctx.TryConsumeRep();
                ctx.Reply("focusing camera on {0}", Pos.String(coord));
                return;
            }

            var searchObjName = Strings.ToSearchString(args);
            var closestDist = float.MaxValue;
            Tile closestObj = null;
            var fromPos = The.Bindings.Mouse.WorldPosition;
            for (int i = S.Grids[WorldLayer.Objects].Data.Length - 1; i >= 0; i--) {
                Tile obj = S.Grids[WorldLayer.Objects].Data[i];
                if (obj != null && !obj.Transform.IsChild && obj.IsConstructed) {
                    if (obj.Definition.SearchNameT == searchObjName) {
                        var dist = Vectors.SqrDistance(obj.Position, fromPos);
                        if (dist < closestDist) {
                            closestDist = dist;
                            closestObj = obj;
                        }
                    }
                }
            }
            if (closestObj != null) {
                EntityUtils.CameraFocusOn(closestObj);
                ctx.TryConsumeRep();
                ctx.Reply("focusing camera on closest {0} at {1}",
                    closestObj.NameOrLabel, Pos.String(closestObj.PosIdx));
                return;
            } else {
                ctx.Reply("cannot find object by name {0}", args);
            }
        }
    }
}
