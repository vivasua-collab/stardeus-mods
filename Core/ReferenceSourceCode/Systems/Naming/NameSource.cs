using System.Collections.Generic;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Naming {
    public abstract class NameSource : IResettable {
        protected static void Register(string id, NameSource src) {
            src.Id = id;
            src.IdH = Hashes.S(id);
            Registries.NameSources.Add(id, src);
        }

        public readonly HashSet<int> UsedNames = new();
        public string Id;
        public int IdH;
        public bool IsDisabled;
        /// <summary>
        /// Called when game is created or loaded. You can set IsDisabled here.
        /// </summary>
        public virtual void Setup(GameState S) { }
        protected void Consume(string name) {
            if (name != null) {
                // D.Warn("Consuming name: {0}", name);
                UsedNames.Add(Hashes.S(name));
            }
        }
        protected bool IsConsumed(string name) {
            return UsedNames.Contains(Hashes.S(name));
        }
        protected void Consume(string part1, string part2) {
            if (part1 != null && part2 != null) {
                // D.Warn("Consuming name parts: {0}+{1}", part1, part2);
                UsedNames.Add(Hashes.Quick(Hashes.S(part1), Hashes.S(part2)));
            }
        }
        protected bool IsConsumed(string part1, string part2) {
            return UsedNames.Contains(Hashes.Quick(Hashes.S(part1), Hashes.S(part2)));
        }

        public abstract bool Supports(NameRequest request);
        public abstract float ChanceFor(NameRequest request);
        public abstract int PriorityFor(NameType type);
        public abstract bool TryNameFor(NameRequest req, out NameResult name);
        protected virtual void Cleanup() {}

        public void OnReset() {
            UsedNames.Clear();
            Cleanup();
        }
    }
}
