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
    public class CmdTwitchPoop : TwitchCommand {
        private const string cmd = "poop";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 15,
                (ctx) => new CmdTwitchPoop(ctx),
                "Makes your colonist relieve itself on the spot");
        }

        public CmdTwitchPoop(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (!being.HasNeed(NeedIdH.Toilet)) {
                ctx.Reply("{0} cannot do that", being.Name);
                return;
            }
            if (being.StatsBeing.Toilet.Value < 0.1f) {
                ctx.Reply("too soon! {0} just did that recently", being.Name);
                return;
            }
            if (ctx.TryConsumeRep()) {
                being.StatsBeing.Toilet.UpdateBase(0f);
                being.Body.AddExcrementsOnFloor();
                ctx.Reply("{0} has pooped at {1}", being.Name, Pos.String(being.Being.PosIdx));
            }
        }
    }
}
