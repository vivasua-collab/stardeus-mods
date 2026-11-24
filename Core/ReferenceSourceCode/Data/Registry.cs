using System.Collections.Generic;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public class Registry<T> {
        public readonly string Handle;
        public Registry() {
            Handle = typeof(T).Name;
        }
        private readonly Dictionary<int, T> registry = new();
        public Dictionary<int, T> All => registry;

        public void Clear() {
            CleanupAfterGame();
            registry.Clear();
        }

        public void CleanupAfterGame() {
            if (typeof(IResettable).IsAssignableFrom(typeof(T))) {
                foreach (IResettable r in registry.Values) {
                    r.OnReset();
                }
            }
        }

        public T Get(string id, bool errorIfMissing = true) {
            return Get(id, Hashes.S(id), errorIfMissing);
        }
        public T FastGet(int idH) {
            if (registry.TryGetValue(idH, out var f)) {
                return f;
            }
            return default;
        }

        public T Get(string id, int idH, bool errorIfMissing = true) {
            if (registry.TryGetValue(idH, out var f)) {
                return f;
            }
            if (errorIfMissing) {
                D.Err("Unknown item: {0}. {1}. {2}", id, idH, Handle);
            }
            return default;
        }

        public void Add(string type, T item) {
            var typeId = Hashes.S(type);
            D.Ass(!registry.ContainsKey(typeId),
                "Registry already contains item {0}!. {1}", type, Handle);
            D.Ass(!registry.ContainsValue(item),
                "Registry already contains item value {0}!. {1}", type, Handle);
            registry[typeId] = item;
        }
    }
}
