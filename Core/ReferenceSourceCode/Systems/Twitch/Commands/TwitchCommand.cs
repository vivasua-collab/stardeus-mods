using Game.Data;
using Game.Systems;
using Game.Commands;
using UnityEngine;
using KL.Utils;
using Game.CodeGen;
using System;
using Game.Systems.Creatures;
using Game.Systems.AI;
using Game.Constants;
using Game.Systems.Items;

namespace Game.Systems.Twitch.Commands {
    public abstract class TwitchCommand : Command {
        protected readonly TwitchCtx ctx;
        public readonly DateTime Time;
        public readonly string Cmd;
        public readonly int CmdH;
        public TwitchCommand(string id, TwitchCtx ctx) : base(CmdToId(id)) {
            Cmd = id;
            CmdH = Hashes.S(id);
            this.ctx = ctx;
            Time = DateTime.UtcNow;
        }

        public static string CmdToId(string cmd) {
            return $"tw_{cmd}";
        }
        public static string IdToCmd(string id) {
            return id.Replace("tw_", string.Empty);
        }

        protected string CommandName(string cmd) {
            return ctx.CommandName(cmd);
        }
        protected bool CheckRegistered() {
            if (ctx.Viewer == null) {
                ctx.Reply("you are not registered. Use {0}", CommandName("join"));
                return false;
            }
            return true;
        }
        protected bool CheckRole(int roleIdH, TwitchViewer viewer = null) {
            viewer ??= ctx.Viewer;
            if (viewer.HasRole(roleIdH) != true) {
                ctx.Reply("doesn't have a required role for command: {0}",
                    TwitchRoleIdH.ResolveName(roleIdH));
                return false;
            }
            return true;
        }
        protected bool TryGetBeing(out BeingData being, bool requireSpawned = true,
                bool skipIncapacitated = true, bool skipUnderDC = true,
                bool skipNonInterruptable = true) {
            being = Being;
            if (being == null) {
                var mindObj = MindOjb;
                if (mindObj != null) {
                    ctx.Reply("your mind ({0}) is currently removed, ask the streamer to install it into a body.",
                        mindObj.Mind.Name.ShortName);
                }
                ctx.Reply("this action requires controlling a being. Use {0}", CommandName("join"));
                return false;
            }
            if (requireSpawned && being.Being == null) {
                ctx.Reply("{0} is currently not available.", being.Name);
                return false;
            }
            if (being.Being.AIAgent != null) {
                if (skipUnderDC && being.Being.AIAgent.DirectControl.IsUnderDirectControl)  {
                    ctx.Reply("{0} is currently not available (Direct Control)", being.Name);
                    return false;
                }
                if (skipNonInterruptable && being.Being.AIAgent.Goal?.IsNonInterruptable == true)  {
                    ctx.Reply("{0} is currently busy: {1}", being.Name, being.Being.AIAgent.Goal.Name);
                    return false;
                }

            }
            if (skipIncapacitated && being.Body.IsIncapacitated) {
                ctx.Reply("{0} is incapacitated.", being.Name);
                return false;
            }
            return true;
        }
        protected bool PrepareForAssigningATask(BeingData being) {
            var agent = being.Being?.AIAgent;
            if (agent == null) {
                return false;
            }
            var goal = agent.Goal;
            if (goal == null) {
                return true;
            }
            if (!AIGoal.Interrupt(being.Being.AIAgent.Goal, G.Ticks)) {
                ctx.Reply("{0} is busy: {1}", being.Name, goal.Name);
                return false;
            }
            return true;
        }
        public TwitchSettings Settings => ctx.Sys.Settings;
        public BeingData Being => ctx.Sys.BeingOf(ctx.Viewer);
        public ObjData MindOjb => ctx.Sys.MindObjOf(ctx.Viewer);
        public string ViewerName => ctx.Viewer?.Name ?? ctx.Msg.Username;
        public string Args => ctx.Msg.Args;
    }
}
