#if !PRODUCTION_BUILD
// #define DEBUG_BINDABLE
#endif
using System;
using System.Collections.Generic;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    /// <summary>
    /// A bindable value that can be observed for changes. Mostly for UI.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class B<T> {
        private struct Slot {
            public Action<T> Listener;
            public int Generation;
            public int NextFree;
            public bool PendingRemoval;
            #if DEBUG_BINDABLE
            public object ListenerObj;
            public string BindTrace;
            #endif
        }

        public struct ListenerHandle : IWithLifetime {
            private readonly B<T> owner;
            private readonly int index;
            private readonly int generation;
            public bool IsDisposed { get; private set; }

            internal ListenerHandle(B<T> owner, int index, int generation) {
                this.owner = owner;
                this.index = index;
                this.generation = generation;
                this.IsDisposed = false;
            }

            public bool IsValid => owner != null &&
                index >= 0 && index < owner.count &&
                owner.slots[index].Generation == generation &&
                !owner.slots[index].PendingRemoval;

            public void Dispose() {
                owner?.Unbind(index, generation);
                IsDisposed = true;
            }
        }

        private T value;
        private Slot[] slots;
        private int count;
        private int freeList;
        protected int numListeners;
        // a depth, not a flag: a listener can set this bindable again from inside Notify
        private int notifyDepth;
        private readonly int maxListeners;

        public B(T initialValue, int capacity = 1) {
            value = initialValue;
            slots = new Slot[capacity];
            freeList = -1;
            if (capacity == -1) {
                capacity = 1;
                maxListeners = 10240;
                return;
            }
            #if PRODUCTION_BUILD
            // leak protection
            maxListeners = 10240;
            #else
            // strict smaller limit for development
            maxListeners = capacity * 2 + 8;
            #endif
        }

        public T Value => value;
        public bool HasListeners => numListeners > 0;

        public bool Set(T newValue) {
            if (EqualityComparer<T>.Default.Equals(value, newValue)) {
                return false;
            }
            value = newValue;
            Notify();
            return true;
        }

        public void SetSilent(T newValue) {
            value = newValue;
        }

        public void ForceNotify() {
            Notify();
        }

        private void Notify() {
            // One throwing listener used to skip the rest and leave the notifying flag set,
            // so later unbinds only marked their slots and nothing was ever freed again.
            notifyDepth++;
            try {
                int n = count;
                for (int i = 0; i < n; i++) {
                    ref Slot slot = ref slots[i];
                    if (slot.Listener != null && !slot.PendingRemoval) {
                        try {
                            slot.Listener(value);
                        } catch (Exception ex) {
                            D.LogEx(ex, "Bindable listener failed: {0}", this);
                        }
                    }
                }
            } finally {
                notifyDepth--;
            }
            if (notifyDepth == 0) {
                ProcessPendingRemovals();
            }
        }

        public ListenerHandle Bind(Lifetime lifetime, Action<T> listener, bool notifyImmediately = true,
                bool allowNullLifetime = false) {
            if (lifetime == null && !allowNullLifetime) {
                throw new ArgumentNullException(nameof(lifetime));
            }
            if (listener == null) {
                throw new ArgumentNullException(nameof(listener));
            }
            // Checked before a slot is taken: Lifetime.Add throwing after the slot was
            // allocated left the listener bound forever, firing on dead widgets.
            if (lifetime != null && lifetime.IsDisposed) {
                D.Err("Trying to bind to disposed Lifetime '{0}': {1}", lifetime.Name, this);
                throw new ObjectDisposedException(nameof(Lifetime));
            }

            int index;
            if (freeList != -1) {
                index = freeList;
                freeList = slots[index].NextFree;
            } else {
                if (count == slots.Length)
                    Grow();
                index = count++;
            }

            #if DEBUG_BINDABLE
            for (int i = slots.Length - 1; i >= 0; i--) {
                Slot sl = slots[i];
                if (sl.ListenerObj == listener.Target && !sl.PendingRemoval) {
                    D.Err("Binding same listener twice! Listener: {0}\nBindTace: {1}\nEnd bind trace.",
                        listener.Target, sl.BindTrace);
                }
            }
            #endif

            ref Slot slot = ref slots[index];
            slot.Listener = listener;
            slot.Generation++;
            #if DEBUG_BINDABLE
            slot.ListenerObj = listener.Target;
            slot.BindTrace = Environment.StackTrace;
            #endif
            numListeners++;
            // D.Warn("Binding: {0} => {1}. Listeners: {2}", listener.Target, value, numListeners);

            // Register with the lifetime before the immediate invoke, so a listener that
            // throws on its first call still gets unbound when the lifetime is disposed,
            // instead of leaking a zombie slot bound forever.
            var handle = new ListenerHandle(this, index, slot.Generation);
            lifetime?.Add(handle);

            if (notifyImmediately) {
                listener(value);
            }

            return handle;
        }

        public void Unbind(int index, int generation) {
            // D.Warn("Unbinding: {0} => {1}", index, generation);
            if (index < 0 || index >= count)
                return;

            ref Slot slot = ref slots[index];
            if (slot.Generation != generation)
                return;

            if (notifyDepth > 0) {
                slot.PendingRemoval = true;
                return;
            }

            DoRemove(index);
        }

        private void ProcessPendingRemovals() {
            for (int i = 0; i < count; i++) {
                ref Slot slot = ref slots[i];
                if (slot.PendingRemoval) {
                    slot.PendingRemoval = false;
                    DoRemove(i);
                }
            }
        }

        private void DoRemove(int index) {
            ref Slot slot = ref slots[index];
            #if DEBUG_BINDABLE
            slot.BindTrace = null;
            slot.ListenerObj = null;
            #endif
            slot.Listener = null;
            // a second Unbind with the same handle would push this slot onto the free
            // list twice (a self-loop), so two later binds would share it
            slot.Generation++;
            slot.NextFree = freeList;
            freeList = index;
            numListeners--;
            // D.Warn("Listeners now: {0}", numListeners);
        }

        private void Grow() {
            #if DEBUG_BINDABLE
            D.Warn("Growing bindable from {0} to {1}: {2}", slots.Length, slots.Length * 2, this);
            #endif
            if (slots.Length >= maxListeners) {
                D.Throw("Trying to grow bindable too much! Something must be unbound {0}", this);
            }
            var newArr = new Slot[slots.Length * 2];
            Array.Copy(slots, newArr, slots.Length);
            slots = newArr;
        }

        public override string ToString() {
            return $"B<{typeof(T).Name}> Value={value} Listeners={numListeners}";
        }
    }
}
