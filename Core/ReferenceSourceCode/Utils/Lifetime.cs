#if !PRODUCTION_BUILD
// #define DEBUG_LIFETIME
#define TRACK_LIFETIMES
#endif
using System;
using System.Collections.Generic;
using Game.Systems.UIUX;
using KL.Utils;

namespace Game.Utils {
    public sealed class Lifetime : IDisposable {
        public readonly string Name;
        IWithLifetime[] items;
        int count;
        Lifetime[] children;
        int childCount;
        bool isDisposed;
        public bool IsDisposed => isDisposed;
        private Action onDispose;
        private Lifetime parent;
        private readonly int initialCapacity;
        private readonly int initialChildCapacity;
        public static List<Lifetime> ActiveLifetimes = new();
        // Position in ActiveLifetimes, for an O(1) swap-remove: List.Remove searched the
        // whole list on every dispose, from the front while the newest sit at the back, so
        // disposing a panel with many child lifetimes (and DisposeAll) was quadratic
        private int activeIndex = -1;

        private static void Register(Lifetime life) {
            life.activeIndex = ActiveLifetimes.Count;
            ActiveLifetimes.Add(life);
        }

        private static void Unregister(Lifetime life) {
            var idx = life.activeIndex;
            if (idx < 0 || idx >= ActiveLifetimes.Count || ActiveLifetimes[idx] != life) {
                // the list was changed from outside: fall back to a search
                ActiveLifetimes.Remove(life);
                life.activeIndex = -1;
                return;
            }
            var lastIdx = ActiveLifetimes.Count - 1;
            if (idx != lastIdx) {
                var last = ActiveLifetimes[lastIdx];
                ActiveLifetimes[idx] = last;
                last.activeIndex = idx;
            }
            ActiveLifetimes.RemoveAt(lastIdx);
            life.activeIndex = -1;
        }

        private Lifetime(string name, int capacity = 8, int childCapacity = 4) {
            Name = name;
            initialCapacity = capacity;
            initialChildCapacity = childCapacity;
            items = new IWithLifetime[capacity];
            children = new Lifetime[childCapacity];
        }

        public Lifetime OnDispose(Action callback) {
            onDispose += callback;
            return this;
        }

        public static Lifetime Create(string name, int capacity = 8, int childCapacity = 4) {
            var life = new Lifetime(name, capacity, childCapacity);
            Register(life);
            return life;
        }

        public Lifetime CreateChild(string name, int capacity = 8, int childCapacity = 4) {
            if (isDisposed) {
                D.Err("Trying to create child Lifetime '{0}' on disposed Lifetime '{1}'", name, Name);
                throw new ObjectDisposedException(nameof(Lifetime));
            }
            if (childCount == children.Length) {
                GrowChildren();
            }
            #if DEBUG_LIFETIME
            D.Log("Lifetime '{0}' creating child '{1}'", Name, name);
            #endif
            #if !PRODUCTION_BUILD
            name = $"{Name}.{name}";
            #endif

            var child = new Lifetime(name, capacity, childCapacity);
            child.parent = this;
            children[childCount++] = child;
            Register(child);
            return child;
        }

        public void Add(IWithLifetime item) {
            if (item == null) {
                D.Err("Trying to add null IWithLifetime to Lifetime '{0}'", Name);
                return;
            }
            if (isDisposed) {
                D.Err("Trying to add IWithLifetime to disposed Lifetime '{0}'", Name);
                throw new ObjectDisposedException(nameof(Lifetime));
            }

            #if DEBUG_LIFETIME
            D.Log("Lifetime '{0}' adding item: {1}", Name, item);
            #endif

            if (count == items.Length) {
                GrowItems();
            }

            items[count++] = item;
        }

        public void Dispose() {
            if (isDisposed) {
                #if DEBUG_LIFETIME
                D.Warn("Lifetime '{0}' already disposed", Name);
                #endif
                return;
            }

            #if DEBUG_LIFETIME
            D.Log("Disposing Lifetime '{0}' with {1} items and {2} children", Name, count, childCount);
            #endif

            isDisposed = true;

            // One failing child or item must not stop the rest: a throw used to leave the
            // remaining widgets on screen, skip onDispose (listeners removed there leaked)
            // and keep this Lifetime in ActiveLifetimes, half disposed.
            // Children first
            for (int i = 0; i < childCount; i++) {
                var ch = children[i];
                if (!ch.isDisposed) {
                    try {
                        ch.Dispose();
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed disposing child Lifetime '{0}' of '{1}'", ch.Name, Name);
                    }
                }
            }
            Array.Clear(children, 0, childCount);

            for (int i = 0; i < count; i++) {
                var item = items[i];
                if (!item.IsDisposed) {
                    try {
                        item.Dispose();
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed disposing an item of Lifetime '{0}': {1}", Name, item);
                    }
                #if DEBUG_LIFETIME
                } else {
                    D.Err("Item in Lifetime '{0}' already disposed: {1}", Name, item);
                #endif
                }
            }

            Array.Clear(items, 0, count);

            childCount = 0;
            count = 0;
            try {
                onDispose?.Invoke();
            } catch (Exception ex) {
                D.LogEx(ex, "Failed invoking OnDispose of Lifetime '{0}'", Name);
            }
            onDispose = null;
            Unregister(this);
            if (parent != null && !parent.isDisposed) {
                parent.RemoveChildRef(this);
            }
            parent = null;
        }

        private void RemoveChildRef(Lifetime child) {
            for (int i = 0; i < childCount; i++) {
                if (children[i] == child) {
                    children[i] = children[childCount - 1];
                    children[childCount - 1] = null;
                    childCount--;
                    return;
                }
            }
        }

        private void GrowItems() {
            #if DEBUG_LIFETIME
            D.Log("Growing items array for Lifetime '{0}' from {1} to {2}", Name, items.Length, items.Length * 2);
            #endif
            var len = items.Length;
            if (len > Math.Max(512, initialCapacity)) {
                D.Err("Lifetime '{0}' has too many items ({1}), possible leak?", Name, len);
            }
            var newArr = new IWithLifetime[len * 2];
            Array.Copy(items, newArr, items.Length);
            items = newArr;
        }

        private void GrowChildren() {
            #if DEBUG_LIFETIME
            D.Log("Growing children array for Lifetime '{0}' from {1} to {2}", Name, children.Length, children.Length * 2);
            #endif
            var len = children.Length;
            if (len > Math.Max(256, initialChildCapacity)) {
                D.Err("Lifetime '{0}' has too many children ({1}), possible leak?", Name, len);
            }
            var newArr = new Lifetime[len * 2];
            Array.Copy(children, newArr, children.Length);
            children = newArr;
        }
        #if TRACK_LIFETIMES
        public void DebugDump() {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Lifetime Dump: {Name}");
            sb.AppendLine($"  Disposed: {isDisposed}");
            sb.AppendLine($"  Items ({count}):");
            for (int i = 0; i < count; i++) {
                sb.AppendLine($"    {items[i]} (Disposed: {items[i].IsDisposed})");
            }
            sb.AppendLine($"  Children ({childCount}):");
            for (int i = 0; i < childCount; i++) {
                sb.AppendLine($"    {children[i]}");
            }
            WPopup.Warning($"Lifetime: {Name}", null, sb.ToString());
        }
        #endif

        public static void DisposeAll() {
            foreach (var life in ActiveLifetimes.ToArray()) {
                life.Dispose();
            }
            D.Ass(ActiveLifetimes.Count == 0,
                "Not all lifetimes were disposed properly");
            ActiveLifetimes.Clear();
        }

        override public string ToString() {
            return $"Lifetime[{Name}](Items:{count}, Children:{childCount}, Disposed:{isDisposed})";
        }

    }
}
