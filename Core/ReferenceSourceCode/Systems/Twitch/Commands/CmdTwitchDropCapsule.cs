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
    public class CmdTwitchDropCapsule : TwitchCommand {
        private const string cmd = "drop_capsule";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 15,
                (ctx) => new CmdTwitchDropCapsule(ctx),
                    "Drops a Storage Capsule with random contents");
        }

        public CmdTwitchDropCapsule(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Story)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Space/StorageCapsule");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("sending a storage capsule!");
                } else {
                    ctx.Reply("failed starting a storage capsule");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
