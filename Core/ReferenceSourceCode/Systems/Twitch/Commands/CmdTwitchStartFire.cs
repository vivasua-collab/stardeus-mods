using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Systems.Firex;
using Game.CodeGen;
using Game.Systems.Creatures;
using Game.Constants;
using Game.Systems.AI;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchStartFire : TwitchCommand {
        private const string cmd = "fire";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 30,
                (ctx) => new CmdTwitchStartFire(ctx),
                "Your being will attempt to start a fire");
        }
        public CmdTwitchStartFire(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Destroy)) {
                return;
            }
            if (!being.HasAbility(AbilityIdH.Manipulate)) {
                ctx.Reply("cannot start a fire, {0} has no ability to manipulate objects", being.Name);
                return;
            }
            int pos = being.Being.Graphics.PosInFrontOf;
            var topmost = EntityUtils.GetTopmostParentTile(pos);
            if (topmost != null && topmost.IsConstructed && !topmost.Flammable.IsOnFire) {
                if (S.Sys.Firex.TrySetOnFire(topmost.Flammable, Fire.CreateBalanced(S))) {
                    ctx.TryConsumeRep();
                    if (AIActionIdleRoam.TryRoam(being.Being.AIAgent, false, 2)) {
                        being.Being.Motion.SetForcedWalk();
                    }
                    ctx.Reply("{0} has set {1} on fire!", being.Name, topmost.NameOrLabel);
                } else {
                    ctx.Reply("{0} failed to set {0} on fire", being.Name, topmost.NameOrLabel);
                }
            } else {
                ctx.Reply("{0} didn't have anything flammable nearby", being.Name);
            }
        }

    }
}
