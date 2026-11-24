using System;
using Game.Commands;

namespace Game.Commands {
    public class CmdExecuteAction : Command {
        private Action action;
        public CmdExecuteAction(Action action) : base("execute_act") {
            this.action = action;
        }
        protected override void Execute() {
            action();
        }
    }
}
