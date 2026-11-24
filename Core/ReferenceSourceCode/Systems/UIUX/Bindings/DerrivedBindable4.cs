using System;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class B4<T1, T2, T3, T4, TResult> : B<TResult>, IWithLifetime {
        private readonly B<T1> source1;
        private readonly B<T2> source2;
        private readonly B<T3> source3;
        private readonly B<T4> source4;
        private readonly IWithLifetime handle1;
        private readonly IWithLifetime handle2;
        private readonly IWithLifetime handle3;
        private readonly IWithLifetime handle4;
        private readonly Func<T1, T2, T3, T4, TResult> derriveFunc;
        public bool IsDisposed { get; private set; }

        public B4(Lifetime lifetime,
            B<T1> source1,
            B<T2> source2,
            B<T3> source3,
            B<T4> source4,
            Func<T1, T2, T3, T4, TResult> derriveFunc
        ) : base(default) {
            lifetime.Add(this);
            this.source1 = source1;
            this.source2 = source2;
            this.source3 = source3;
            this.source4 = source4;
            this.derriveFunc = derriveFunc;

            handle1 = source1.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle2 = source2.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle3 = source3.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle4 = source4.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);

            // Initial value
            UpdateValue();
        }

        private void UpdateValue() {
            if (IsDisposed) {
                D.Err("UpdateValue called on disposed B4!");
                return;
            }
            Set(derriveFunc(source1.Value, source2.Value, source3.Value, source4.Value));
        }

        public void Dispose() {
            if (!IsDisposed) {
                handle1.Dispose();
                handle2.Dispose();
                handle3.Dispose();
                handle4.Dispose();
                IsDisposed = true;
            }
        }
    }
}
