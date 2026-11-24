using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;

namespace Game.Commands {
    public class CmdRemoveBeing : Command {
        private readonly Being toRemove;

        public CmdRemoveBeing(Being b) : base("remove_being") {
            toRemove = b;
            b.IsActive = false;
        }

        protected override void Execute() {
            if (!GameState.IsCurrent(S)) {
                D.Err("Trying to execute CmdRemoveBeing from a different game");
                return;
            }
            S.Beings.Destroy(toRemove, true);
        }
    }
}
