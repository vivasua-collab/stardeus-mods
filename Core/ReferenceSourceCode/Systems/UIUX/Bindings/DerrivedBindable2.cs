using System;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Use it when you want to bind a bindable that derrives its value from two other bindables.
    /// </summary>
    public class B2<T1, T2, TResult> : B<TResult>, IWithLifetime {
        private readonly B<T1> source1;
        private readonly B<T2> source2;
        private readonly IWithLifetime handle1;
        private readonly IWithLifetime handle2;
        private readonly Func<T1, T2, TResult> derriveFunc;
        public bool IsDisposed { get; private set; }

        public B2(Lifetime lifetime,
            B<T1> source1,
            B<T2> source2,
            Func<T1, T2, TResult> derriveFunc
        ) : base(default) {
            lifetime.Add(this);
            this.source1 = source1;
            this.source2 = source2;
            this.derriveFunc = derriveFunc;

            handle1 = source1.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);
            handle2 = source2.Bind(null, (_) => UpdateValue(), allowNullLifetime: true);

            // Initial value
            UpdateValue();
        }

        private void UpdateValue() {
            if (IsDisposed) {
                D.Err("UpdateValue called on disposed B2!");
                return;
            }
            Set(derriveFunc(source1.Value, source2.Value));
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
