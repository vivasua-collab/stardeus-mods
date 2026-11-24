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
    public class CmdTwitchAsteroid : TwitchCommand {
        private const string cmd = "asteroid";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 900,
                (ctx) => new CmdTwitchAsteroid(ctx),
                    "Spawns an Asteroid");
        }

        public CmdTwitchAsteroid(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Destroy)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Space/Asteroid");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("sending an Asteroid!");
                } else {
                    ctx.Reply("failed sending an Asteroid");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
