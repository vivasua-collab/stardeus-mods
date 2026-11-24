using System;
using System.Collections;
using Game.Data;
using KL.Utils;
namespace Game.Commands {
    /// <summary>
    /// If you use this, you will get herpes in your butt.
    /// </summary>
    public abstract class AsyncCommand : Command {
        protected AsyncCommand(string id, bool unique = false, bool system = false) :
            base(id, unique, system) {
                CrtName = Id;
            }

        public AsyncCommand WithState(GameState s) {
            S = s;
            return this;
        }
        public abstract IEnumerator ExecuteAsync();
        public string CrtName;
        public abstract bool TrySameFrame { get; }
        protected override void Execute() {
            if (S == null) {
                D.Err("Trying to invoke async command without game state!");
            }
            Coroutines.Start(ExecuteAsync(), this, CrtName, TrySameFrame, IsSystem);
        }
    }
}
