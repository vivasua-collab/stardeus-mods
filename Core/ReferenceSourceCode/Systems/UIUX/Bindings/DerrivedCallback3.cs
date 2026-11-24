using System;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class C3<T1, T2, T3> : IWithLifetime {
        private readonly B<T1> source1;
        private readonly B<T2> source2;
        private readonly B<T3> source3;
        private readonly IWithLifetime handle1;
        private readonly IWithLifetime handle2;
        private readonly IWithLifetime handle3;
        private readonly Action<T1, T2, T3> callback;
        public bool IsDisposed { get; private set; }

        public C3(Lifetime lifetime,
            B<T1> source1,
            B<T2> source2,
            B<T3> source3,
            Action<T1, T2, T3> callback
        ) {
            lifetime.Add(this);
            this.source1 = source1;
            this.source2 = source2;
            this.source3 = source3;
            this.callback = callback;

            handle1 = source1.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle2 = source2.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle3 = source3.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);

            // Initial value
            UpdateValue();
        }

        private void UpdateValue() {
            if (IsDisposed) {
                D.Err("UpdateValue called on disposed C3!");
                return;
            }
            callback(source1.Value, source2.Value, source3.Value);
        }

        public void Dispose() {
            if (!IsDisposed) {
                handle1.Dispose();
                handle2.Dispose();
                handle3.Dispose();
                IsDisposed = true;
            }
        }
    }
}
