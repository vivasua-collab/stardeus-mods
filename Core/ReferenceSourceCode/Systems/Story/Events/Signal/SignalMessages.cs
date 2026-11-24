using System.Collections.Generic;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Story.Events {
    public static class SignalMessages {
        [SelfInit]
        private static void Cleanup() {
            keysForRoot.Clear();
            msgsForRoot.Clear();
        }

        private static readonly Dictionary<int, string[]> keysForRoot = new();
        private static readonly Dictionary<int, string[]> msgsForRoot = new();

        public static bool TryRandomMsgFor(string root, Rng rng, out string msg) {
            if (string.IsNullOrEmpty(root)) {
                D.Err("Trying to get a random message for null root");
                msg = null;
                return false;
            }

            var rootKey = Hashes.S(root);
            if (!msgsForRoot.TryGetValue(rootKey, out var msgs)) {
                msgs = LoadKeysFor(root, true);
                msgsForRoot.Add(rootKey, msgs);
            }
            if (msgs == null) {
                D.Err("Trying to get a random message for root: {0} -> there are no options!",
                    root);
                msg = null;
                return false;
            }
            msg = rng.From(msgs);
            return true;
        }

        public static bool TryRandomKeyFor(string root, Rng rng, out string msg) {
            if (string.IsNullOrEmpty(root)) {
                D.Err("Trying to get a random message for null root");
                msg = null;
                return false;
            }

            var rootKey = Hashes.S(root);
            if (!keysForRoot.TryGetValue(rootKey, out var msgs)) {
                msgs = LoadKeysFor(root, false);
                keysForRoot.Add(rootKey, msgs);
            }
            if (msgs == null) {
                D.Err("Trying to get a random message for root: {0} -> there are no options!",
                    root);
                msg = null;
                return false;
            }
            msg = rng.From(msgs);
            return true;
        }

        private static string[] LoadKeysFor(string root, bool translate) {
            int i = 0;
            int attempts = 999;
            int gap = 0;
            int foundKeys = 0;
            Caches.StringList.Clear();
            while (attempts-- > 0) {
                var key = $"{root}.{i++}";
                if (key.HasT()) {
                    if (gap > 0) {
                        D.Err("There is a gap before hint key: {0}.{1}", key);
                        gap = 0;
                    }
                    foundKeys++;
                    if (translate) {
                        key = key.T();
                    }
                    Caches.StringList.Add(key);
                } else if (gap++ > 3) {
                    break;
                }
            }
            if (Caches.StringList.Count > 0) {
                return Caches.StringList.ToArray();
            }
            return null;
        }
    }
}
