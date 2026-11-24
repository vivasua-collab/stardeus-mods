using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Story.Events;
using Game.UI;
using Game.CodeGen;
using Game.Data.Factions;
using Game.Constants;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchBreakdown : TwitchCommand {
        private const string cmd = "breakdown";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 150,
                (ctx) => new CmdTwitchBreakdown(ctx),
                    "Starts a mental breakdown");
        }

        public CmdTwitchBreakdown(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (being.Mind.Health == null) {
                ctx.Reply("{0} is unable to have mental breakdowns.", being.Name);
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }
            if (!PrepareForAssigningATask(being)) {
                return;
            }
            if (being.Mind.Health.TryStartBreakdown(being.Being, true)) {
                ctx.TryConsumeRep();
                ctx.Reply("starting a mental breakdown!");
            } else {
                ctx.Reply("{0} was unable to start a mental breakdown!", being.Name);
            }
        }
    }
}
