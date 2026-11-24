using System.Collections;
using Game.Data;
using Game.Systems;
using Game.Commands;
using KL.Text;
using UnityEngine;
using Game.Constants;
using Game.CodeGen;
using KL.Utils;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchBuild : TwitchCommand {
        private const string cmd = "build";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 300,
                (ctx) => new CmdTwitchBuild(ctx),
                "Orders building a device or structure at current position");
        }

        public CmdTwitchBuild(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Build)) {
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }
            if (!being.HasAbility(AbilityIdH.Build)) {
                ctx.Reply("{0} has no ability to build", being.Name);
                return;
            }
            var args = ctx.Msg.Args;
            if (!The.Defs.TryFindByName(args, out var def)) {
                ctx.Reply("couldn't find a device or structure named: {0}", args);
                return;
            }
            if (def.LayerId >= WorldLayer.FreeEntities) {
                ctx.Reply("cannot build {0}, try something else", def.NameT);
                return;
            }

            if (!S.Research.IsDefUnlocked(def)) {
                ctx.Reply("cannot build {0}, research locked", def.NameT);
                return;
            }

            var t = EntityUtils.CreatePrototype<Tile>(def);
            if (t == null) {
                ctx.Reply("failed building {0}", def.NameT);
                return;
            }
            var f = Facing.Opposite(being.Being.Graphics.CurrentFacing);
            t.Transform.TrySetRotation(f);
            if (t == null) {
                ctx.Reply("failed building {0}", def.NameT);
                return;
            }
            var pos = being.Being.Graphics.PosInFrontOf;
            var cmd = new CmdPlaceTile(pos, t, warn: false, false, false);
            cmd.Execute(ctx.Sys.S);
            if (cmd.PlacedTile != null) {
                ctx.Reply("{0} has ordered building {1} at {2}", being.Name,
                    def.NameT, Pos.String(pos));
                ctx.TryConsumeRep();
            } else {
                ctx.Reply("could not order building {1} at {2}", being.Name,
                    def.NameT, Pos.String(pos));
            }
        }
    }
}
