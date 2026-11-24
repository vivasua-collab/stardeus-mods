using Game.Data;
using KL.Utils;

namespace Game.Commands {
    public abstract class Command {
        protected GameState S;
        // A copy of this command cannot run in parallel if it's unique, will error out
        public long EnqueueTick;
        public readonly string Id;
        public readonly int IdH;
        protected Command(string id, bool unique = false, bool system = false) {
            if (system) {
                D.Log("Creating system command: {0}", id);
            }
            Id = id;
            IdH = Hashes.S(Id);
            IsUnique = unique;
            IsSystem = system;
        }
        public readonly bool IsUnique;
        public readonly bool IsSystem;
        public string Trace;
        public void Execute(GameState s) {
            if (s == null) {
                D.Err("Trying to execute command with null state! {0}", this);
            }
            S = s;
            Execute();
        }
        protected abstract void Execute();
        public virtual void OnForbidden(GameState s) {}
        public override string ToString() {
            return string.Format("{0} [{1}]", GetType(), Id);
        }
    }
}
