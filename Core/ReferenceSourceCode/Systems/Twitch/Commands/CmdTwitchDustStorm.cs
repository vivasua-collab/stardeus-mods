using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Story.Events;
using Game.UI;
using Game.CodeGen;
using Game.Data.Factions;
using Game.Constants;
using Game.Systems.Story;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchDustStorm : TwitchCommand {
        private const string cmd = "dust_storm";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 30,
                (ctx) => new CmdTwitchDustStorm(ctx),
                    "Creates a Dust Storm");
        }

        public CmdTwitchDustStorm(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Story)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Space/DustStorm");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("starting a dust storm!");
                } else {
                    ctx.Reply("failed to start a dust storm");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
