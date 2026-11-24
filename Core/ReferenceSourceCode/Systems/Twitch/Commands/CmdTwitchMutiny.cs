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
    public class CmdTwitchMutiny : TwitchCommand {
        private const string cmd = "mutiny";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 600,
                (ctx) => new CmdTwitchMutiny(ctx),
                    "Starts a mutiny");
        }

        public CmdTwitchMutiny(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (being.Mind.Traits.HasTrait(TraitIdH.Pacifist)) {
                ctx.Reply("cannot mutiny - you have a Pacifist trait!");
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Destroy)) {
                return;
            }
            if (!ctx.CheckRep()) {
                return;
            }
            if (!PrepareForAssigningATask(being)) {
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Social/Mutiny");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("sending a mutiny!");
                } else {
                    ctx.Reply("failed starting a mutiny");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
