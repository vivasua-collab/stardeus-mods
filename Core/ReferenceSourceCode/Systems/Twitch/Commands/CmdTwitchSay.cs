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
    public class CmdTwitchSay : TwitchCommand {
        private const string cmd = "say";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 10,
                (ctx) => new CmdTwitchSay(ctx),
                "Makes your being say something in-game");
        }
        private const float MessageDuration = 5;

        public CmdTwitchSay(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Talk)) {
                return;
            }
            var args = Args;
            if (string.IsNullOrWhiteSpace(args)) {
                ctx.Reply("usage: {0} <message>", CommandName(cmd));
                return;
            }
            if (being.Body.TryGetSlot(BodyPartSlotIdH.Jaw, out var jaw)) {
                if (jaw.IsPartRemoved || jaw.State.IsDestroyed) {
                    ctx.Reply("{0} cannot speak, jaw is missing or damaged",
                        being.Name);
                    return;
                }
            }
            if (!ctx.TryConsumeRep()) {
                return;
            }
            being.Being.Name.Say(Texts.Accented(args), 15f);
        }
    }
}
