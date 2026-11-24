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
    public class CmdTwitchCleaningIncident : TwitchCommand {
        private const string cmd = "cleaning_incident";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 30,
                (ctx) => new CmdTwitchCleaningIncident(ctx),
                    "Creates a Cleaning Incident");
        }

        public CmdTwitchCleaningIncident(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Story)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Main/CleaningIncident");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("cleaning incident is imminent!");
                } else {
                    ctx.Reply("failed to trigger cleaning incident");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
