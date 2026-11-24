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
    public class CmdTwitchMeteorShower : TwitchCommand {
        private const string cmd = "meteor_shower";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 120,
                (ctx) => new CmdTwitchMeteorShower(ctx),
                    "Creates a Meteor Shower");
        }

        public CmdTwitchMeteorShower(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Destroy)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Space/MeteoroidShower");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("starting a meteoroid shower!");
                } else {
                    ctx.Reply("failed to start a meteoroid shower");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
