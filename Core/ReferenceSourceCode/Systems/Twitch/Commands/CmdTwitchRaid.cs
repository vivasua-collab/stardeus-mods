using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Story.Events;
using Game.UI;
using Game.CodeGen;
using Game.Data.Factions;
using Game.Constants;
using KL.Utils;
using Game.Systems.Story;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchRaid : TwitchCommand {
        private const string cmd = "raid";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 900,
                (ctx) => new CmdTwitchRaid(ctx),
                    "Sends a breach capsule with raiders");
        }

        public CmdTwitchRaid(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Destroy)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Raid/BreachCapsuleRaid");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("sending a breach capsule!");
                } else {
                    ctx.Reply("failed sending a breach capsule");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
