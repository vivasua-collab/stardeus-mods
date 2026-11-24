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
using Game.UI;

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchAnnounce : TwitchCommand {
        private const string cmd = "announce";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 300,
                (ctx) => new CmdTwitchAnnounce(ctx),
                "Shows an announcement in-game");
        }
        public CmdTwitchAnnounce(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
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
            if (!ctx.TryConsumeRep()) {
                return;
            }
            S.Sig.ShowFanfare.Send($"{ViewerName}: {Texts.Accented(args)}");
        }
    }
}
