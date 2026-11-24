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

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchGive : TwitchCommand {
        private const string cmd = "give";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 30,
                (ctx) => new CmdTwitchGive(ctx),
                "Makes your being give an item to the colony");
        }

        public CmdTwitchGive(TwitchCtx ctx) : base(cmd, ctx) { }

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

            var args = ctx.Msg.Args;
            if (MatType.TryFindByName(args, out var mat)) {
                var spawned = EntityUtils.SpawnRawMaterial(mat, 1,
                    being.Being.Graphics.PosInFrontOf, 0.5f, false, true);
                if (spawned != null) {
                    ctx.Reply("{0} has donated {1} to the colony!", being.Name, mat.NameT);
                    ctx.TryConsumeRep();
                } else {
                    ctx.Reply("failed to spawn {0}", mat.NameT);
                }
                return;
            }
            if (!The.Defs.TryFindByName(args, out var def)) {
                ctx.Reply("couldn't find an object or material named: {0}", args);
                return;
            }
            if (def.IsBeing || def.LayerId < WorldLayer.FreeEntities) {
                ctx.Reply("cannot give {0}, try something else", def.NameT);
                return;
            }
            var obj = ctx.Sys.S.Objs.Create(being.Being.Position, def);
            if (obj != null) {
                ctx.Reply("{0} has donated {1} to the colony!", being.Name, def.NameT);
                ctx.TryConsumeRep();
            } else {
                ctx.Reply("failed to spawn {0}", def.NameT);
            }
        }
    }
}
