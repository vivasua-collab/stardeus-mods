using System;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public struct DisposableCallback : IWithLifetime {
        private readonly Action callback;
        public bool IsDisposed { get; private set; }
        public DisposableCallback(Action callback) {
            this.callback = callback;
            IsDisposed = false;
        }

        public void Dispose() {
            if (!IsDisposed) {
                // D.Warn("Disposing DisposableCallback: {0}", callback.Target);
                callback.Invoke();
                IsDisposed = true;
            }
        }
    }
}
