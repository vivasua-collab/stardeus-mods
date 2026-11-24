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
    public class CmdRemoveObj : Command {
        private readonly Obj objToRemove;

        public CmdRemoveObj(Obj obj) : base("remove_obj") {
            objToRemove = obj;
            obj.IsActive = false;
        }

        protected override void Execute() {
            if (!GameState.IsCurrent(S)) {
                D.Err("Trying to execute CmdRemoveTile from a different game");
                return;
            }
            S.Objs.Destroy(objToRemove, true);
        }
    }
}
