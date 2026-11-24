using System.Collections;
using Game.Data;
using Game.Systems;
using Game.Commands;
using KL.Text;
using UnityEngine;
using Game.Constants;
using Game.CodeGen;
using KL.Utils;
using Game.Utils;
using KL.Grid;
using Game.Systems.AI;

namespace Game.Systems.Twitch.Commands {
    public class CmdTwitchClean : TwitchCommand {
        private const string cmd = "clean";

        [SelfInit]
        protected static void Register() {
            TwitchSys.RegisterCommand(cmd, 15,
                (ctx) => new CmdTwitchClean(ctx),
                "Orders your being to clean floors");
        }

        public CmdTwitchClean(TwitchCtx ctx) : base(cmd, ctx) { }

        protected override void Execute() {
            if (!CheckRegistered()) {
                return;
            }
            if (!TryGetBeing(out var being)) {
                return;
            }
            if (!being.Being.AIAgent.CanWork) {
                ctx.Reply("{0} cannot work right now", being.Name);
                return;
            }
            if (!being.Being.AIAgent.Jobs.CanDo(JobTypeIdH.Cleaning)) {
                ctx.Reply("{0} cannot clean", being.Name);
                return;
            }
            if (S.Sys.Dirt.DirtCount == 0)  {
                ctx.Reply("there is no dirt right now");
                return;
            }
            if (!S.Sys.Dirt.HasDirtReachableBy(being.Being)) {
                ctx.Reply("there is no dirt reachable by {0}", being.Name);
                return;
            }

            if (!ctx.CheckRep()) {
                return;
            }
            if (!PrepareForAssigningATask(being)) {
                return;
            }

            var ticks = G.Ticks;
            var forcedGoal = AIGoal.DirectOrder(JobTypeIdH.Cleaning,
                    IconId.CMop, T.CleanFloors, being.Being.AIAgent, ticks)
                // A bit of a hack, initial world state can't be empty
                // this way this works for both idle cleaning and forced cleaning
                .WithVar(AIGoalVars.TargetPos, being.Being.PosIdx)
                .WithWorld(AIVarsH.IsIdle, false)
                .WithDesired(AIVarsH.IsFloorClean, true);
            if (S.Query.ForceGoal.Ask(forcedGoal, true)) {
                ctx.TryConsumeRep();
                ctx.Reply("{0} started cleaning", being.Name);
            } else {
                ctx.Reply("{0} could not clean right now", being.Name);
            }
        }
    }
}
