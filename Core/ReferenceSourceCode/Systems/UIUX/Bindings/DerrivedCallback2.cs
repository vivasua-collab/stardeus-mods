using System;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Use it when you want to call a callback when any of two bindables change.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    public class C2<T1, T2> : IWithLifetime {
        private readonly B<T1> source1;
        private readonly B<T2> source2;
        private readonly IWithLifetime handle1;
        private readonly IWithLifetime handle2;
        private readonly Action<T1, T2> callback;
        public bool IsDisposed { get; private set; }

        public C2(Lifetime lifetime,
            B<T1> source1,
            B<T2> source2,
            Action<T1, T2> callback
        ) {
            lifetime.Add(this);
            this.source1 = source1;
            this.source2 = source2;
            this.callback = callback;

            handle1 = source1.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle2 = source2.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);

            // Initial value
            UpdateValue();
        }

        private void UpdateValue() {
            if (IsDisposed) {
                D.Err("UpdateValue called on disposed C2!");
                return;
            }
            callback(source1.Value, source2.Value);
        }

        public void Dispose() {
            if (!IsDisposed) {
                handle1.Dispose();
                handle2.Dispose();
                IsDisposed = true;
            }
        }
    }
}
