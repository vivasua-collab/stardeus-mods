using System;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class ChangeIntent<T> : IWithLifetime {
        private Action<T> handler;
        public bool IsDisposed => handler != null;

        public void Bind(Action<T> handler) {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Send(T value) {
            handler?.Invoke(value);
        }

        public bool TrySend(T value) {
            if (handler == null) return false;
            handler.Invoke(value);
            return true;
        }

        public void Dispose() {
            handler = null;
        }
    }
}
