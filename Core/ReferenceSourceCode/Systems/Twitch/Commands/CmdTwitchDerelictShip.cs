using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using Game.Story.Events;
using Game.UI;
using Game.CodeGen;
using Game.Data.Factions;
using Game.Constants;
using Game.Procgen;
using KL.Randomness;
using Game.Systems.Story;

using Game.Utils;
namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchDerelictShip : TwitchCommand {
        private const string cmd = "derelict_ship";


        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 120,
                (ctx) => new CmdTwitchDerelictShip(ctx),
                    "Spawns an Derelict Ship");
        }

        public CmdTwitchDerelictShip(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!CheckRole(TwitchRoleIdH.Story)) {
                return;
            }
            if (A.IsInHyperspace) {
                ctx.Reply("can't spawn a derelict while in hyperspace");
                return;
            }
            if (ctx.TryConsumeRep()) {
                var data = S.Query.TriggerStoryEvent.Ask("Space/SpawnDerelictShip");
                if (StoryEventData.IsOK(data)) {
                    ctx.Reply("spawned a Derelict Ship!");
                } else {
                    ctx.Reply("couldn't spawn a Derelict Ship");
                    ctx.UnconsumeRep();
                }
            }
        }
    }
}
