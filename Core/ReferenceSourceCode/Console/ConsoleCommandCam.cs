using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using Game.CodeGen;
using Game.Rendering;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandCam : ConsoleCommand {
        [SelfInit]

        private static void Load() {
            Register(new ConsoleCommandCam());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var cmd = args.GetString(1);
            if (cmd == "info") {
                return OK("Camera position: {0}. Zoom: {1}", RenderUtils.CameraPosition, RenderUtils.CameraOrtho);
            }
            if (cmd == "zoom") {
                var zoom = args.GetFloat(2);
                The.R.Sig.CameraFocus.Send(RenderUtils.CameraPosition, Vector2.one, zoom * 2f);
            }
            if (cmd == "show") {
                var pos = args.GetInt(1);
                if (args.HasArgument(2)) {
                    pos = Pos.FromXY(args.GetInt(1), args.GetInt(2));
                }
                if (pos == 0) {
                    pos = EntityUtils.ToPos(The.R.State.BB.Get(
                        BBKeyH.CameraPosition, Vector2.zero));
                }
                var zoom = Consts.CameraFocusZoomLevel;
                if (args.HasArgument(3)) {
                    zoom = args.GetFloat(3);
                }

                The.R.Sig.CameraFocus.Send(EntityUtils.CenterOf(pos), Vector2.one, zoom);
            }
            return Close();
        }

        public override void Initialize() {
            Name = "cam";
            HelpLine = "Controls camera.";
            Args = new Argument[] {
                new Argument {
                    Name = "action",
                    Help = "<show|zoom>",
                }
            };
        }
    }
}
