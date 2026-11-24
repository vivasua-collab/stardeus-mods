using System;
using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Commands {
    public class CmdLambda : Command {
        private readonly Action action;
        private readonly string what;
        private readonly object context;

        public CmdLambda(Action act, string what, object context) : base("lambda") {
            this.context = context;
            this.what = what;
            if (act == null) {
                D.Err("Creating CmdLambda with null action! {0} / {1}",
                    what, context);
            }
            action = act;
        }

        protected override void Execute() {
            if (action == null) { return; }
            string method = null;
            try {
                method = action.Method?.Name;
                //D.Log("Executing lambda: {0}: {1}. {2}", what, method, context);
                action();
            } catch (Exception ex) {
                D.LogEx(ex, "Failed executing lambda: {0}: {1}.{2}",
                    what, method, context);
            }
        }
    }
}
