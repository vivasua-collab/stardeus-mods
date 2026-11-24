using System;
using Game.Utils;

namespace Game.Systems.UIUX {

    public sealed class BCallback : B<int> {
        public BCallback(int capacity = 1) : base(0, capacity) { }

        // Invoke all callbacks
        public void Invoke() => ForceNotify();
        [Obsolete("Use Invoke() instead", error: true)]
        public new void Set(int value) { }

        // Subscribe with Action (hides Unit from API)
        public ListenerHandle Bind(Lifetime lifetime, Action callback, bool invokeImmediately = true) {
            return Bind(lifetime, _ => callback(), invokeImmediately);
        }

        public override string ToString() {
            return $"BCallback[L:{numListeners}]";
        }
    }
}
