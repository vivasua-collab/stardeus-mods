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
    public class CmdTwitchSpawnEgg : TwitchCommand {
        private const string cmd = "spawn_egg";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 60,
                (ctx) => new CmdTwitchSpawnEgg(ctx),
                    "Spawns an Egg");
        }

        public CmdTwitchSpawnEgg(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Story)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Space/SpawnEgg");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("spawning an egg!");
                } else {
                    ctx.Reply("failed to spawn an egg");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
