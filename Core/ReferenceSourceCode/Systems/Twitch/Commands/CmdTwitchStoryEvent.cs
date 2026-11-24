#if !STARDEUS_DEMO
using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Story.Events;
using Game.UI;
using Game.CodeGen;
using Game.Systems.Story;

using Game.Utils;
using Game.Systems.UIUX;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchStoryEvent : TwitchCommand {
        private const string cmd = "story";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 1200,
                (ctx) => new CmdTwitchStoryEvent(ctx),
                    "Creates a story event by id");
        }

        public CmdTwitchStoryEvent(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Story)) {
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }
            var id = Args;
            var ev = StoryEventDef.Find(id, true);
            if (ev == null) {
                ctx.Reply("can't find story event by id: {0}", id);
            } else {
                if (ctx.TryConsumeRep()) {
                    DevMenuTab_StoryEvents.CreateEventFrom(The.R.State, ev);
                    ctx.Reply("attempting to start a story event: {0}", ev.Id);
                }
            }
        }
    }
}
#endif
