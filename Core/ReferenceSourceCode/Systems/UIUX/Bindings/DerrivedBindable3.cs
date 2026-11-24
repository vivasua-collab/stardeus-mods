using System;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class B3<T1, T2, T3, TResult> : B<TResult>, IWithLifetime {
        private readonly B<T1> source1;
        private readonly B<T2> source2;
        private readonly B<T3> source3;
        private readonly IWithLifetime handle1;
        private readonly IWithLifetime handle2;
        private readonly IWithLifetime handle3;
        private readonly Func<T1, T2, T3, TResult> derriveFunc;
        public bool IsDisposed { get; private set; }

        public B3(Lifetime lifetime,
            B<T1> source1,
            B<T2> source2,
            B<T3> source3,
            Func<T1, T2, T3, TResult> derriveFunc
        ) : base(default) {
            lifetime.Add(this);
            this.source1 = source1;
            this.source2 = source2;
            this.source3 = source3;
            this.derriveFunc = derriveFunc;

            handle1 = source1.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle2 = source2.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle3 = source3.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);

            // Initial value
            UpdateValue();
        }

        private void UpdateValue() {
            if (IsDisposed) {
                D.Err("UpdateValue called on disposed B3!");
                return;
            }
            Set(derriveFunc(source1.Value, source2.Value, source3.Value));
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
