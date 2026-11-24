using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using System.Text;
using Game.Input;
using KL.Grid;
using Game.Systems.Path;
using Game.Constants;
using KL.Utils;
using UnityEngine.Assertions.Must;
using Game.Systems.AI;
using Game.Systems.Creatures;
using System;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchGoTo : TwitchCommand {
        private const string cmd = "goto";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 1,
                (ctx) => new CmdTwitchGoTo(ctx),
                    "Orders the being to go to a position or a nearest object by name");
        }
        public CmdTwitchGoTo(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }
            if (!PrepareForAssigningATask(being)) {
                return;
            }
            var args = Args;
            if (Pos.TryParse(args, out var coord)) {
                if (!being.Being.Motion.TryGoTo(coord,
                        PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby,
                        out var prog)) {
                    ctx.Reply("{0} cannot go to {1}: {2}", being.Name, Pos.String(coord), prog.FailReason);
                    return;
                }
                ctx.TryConsumeRep();
                ctx.Reply("{0} is going to {1}", being.Name, Pos.String(coord));
                being.Being.Motion.SetForcedWalk();
                return;
            }

            // find object
            var searchObjName = Strings.ToSearchString(args);
            var closestDist = float.MaxValue;
            Tile closestObj = null;
            var beingPos = being.Being.PosIdx;
            for (int i = S.Grids[WorldLayer.Objects].Data.Length - 1; i >= 0; i--) {
                Tile obj = S.Grids[WorldLayer.Objects].Data[i];
                if (obj != null && !obj.Transform.IsChild && obj.IsConstructed) {
                    if (obj.Definition.SearchNameT == searchObjName) {
                        var dist = Pos.SqrDistance(obj.Transform.WorkSpotOrPos, beingPos);
                        if (dist < closestDist) {
                            closestDist = dist;
                            closestObj = obj;
                        }
                    }
                }
            }
            if (closestObj != null) {
                if (!being.Being.Motion.TryGoTo(closestObj,
                        PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby,
                        out var prog)) {
                    ctx.Reply("{0} cannot go to {1} at {2}: {3}", being.Name,
                        closestObj.NameOrLabel, Pos.String(closestObj.PosIdx), prog.FailReason);
                    return;
                }
                being.Being.Motion.SetForcedWalk();
                ctx.TryConsumeRep();
                ctx.Reply("{0} is going to {1} at {2}", being.Name,
                    closestObj.NameOrLabel, Pos.String(closestObj.PosIdx));
                return;
            } else {
                ctx.Reply("cannot find an object by name {0}", args);
            }
        }

    }
}
