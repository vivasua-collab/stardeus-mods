using System;
using Game.Constants;
using Game.Systems.Mental;
using KL.Utils;

namespace Game.Systems.Naming {
    public sealed class NameResult {
        /// <summary>
        /// Name if generic
        /// </summary>
        public string Name { get; private set; }
        public BeingName BeingName { get; private set; }
        /// <summary>
        /// Used by NameSource to store the parts of the name
        /// so it can be consumed part by part
        /// </summary>
        public object InternalResult { get; private set; }
        /// <summary>
        /// To pass the owner of the name to the system
        /// </summary>
        public object NameOwner { get; private set; }
        public int Hash { get; private set; }

        private readonly Action<NameResult> onConsume;
        public NameResult(Action<NameResult> onConsume) {
            this.onConsume = onConsume;
        }
        public void SetName(string name) {
            Name = name;
            Hash = Hashes.S(name);
        }
        public void SetBeingName(BeingName name, object internalResult = null) {
            BeingName = name;
            BeingName.NameResult = this;
            InternalResult = internalResult;
            int h = 0;
            if (name.FirstName != null) {
                h = Hashes.S(name.FirstName);
            }
            if (name.LastName != null) {
                h = Hashes.Quick(h, Hashes.S(name.LastName));
            }
            if (name.Nickname != null) {
                h = Hashes.Quick(h, Hashes.S(name.Nickname));
            }
            if (h == 0) {
                D.Err("Setting blank name for being! Internal result: {0}", internalResult);
                name.Nickname = T.Nameless;
                h = Hashes.S(T.Nameless);
            }
            Hash = h;
        }
        public void Consume(object owner = null) {
            NameOwner = owner;
            onConsume?.Invoke(this);
        }

        public override string ToString() {
            return $"NameResult[{Name}|{BeingName}|{InternalResult}]";
        }
    }
}
