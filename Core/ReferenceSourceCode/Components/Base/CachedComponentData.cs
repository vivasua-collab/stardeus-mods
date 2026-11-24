using System.Collections.Generic;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public sealed class CachedComponentData {
        private readonly ComponentData source;
        public CachedComponentData(ComponentData source) {
            if (source == null) {
                D.Err("Trying to cache component data source that is null!");
                return;
            }
            this.source = source;
            if (source.Bools != null) {
                foreach (var kv in source.Bools) {
                    bools.Add(Hashes.S(kv.Key), kv.Value);
                }
            }
            if (source.Ints != null) {
                foreach (var kv in source.Ints) {
                    ints.Add(Hashes.S(kv.Key), kv.Value);
                }
            }
            if (source.Strings != null) {
                foreach (var kv in source.Strings) {
                    strings.Add(Hashes.S(kv.Key), kv.Value);
                }
            }
            if (source.Floats != null) {
                foreach (var kv in source.Floats) {
                    floats.Add(Hashes.S(kv.Key), kv.Value);
                }
            }
        }

        private Dictionary<int, bool> bools = new();
        private Dictionary<int, float> floats = new();
        private Dictionary<int, int> ints = new();
        private Dictionary<int, string> strings = new();
        private Dictionary<int, long> longs = new();

        public bool GetBool(string skey, int hkey, bool def) {
            if (bools.TryGetValue(hkey, out var res)) {
                return res;
            }
            // cache miss
            res = source.GetBool(skey, def);
            // write cache
            bools[hkey] = res;
            return res;
        }

        public void SetBool(int hkey, bool val) {
            bools[hkey] = val;
        }

        public void UnsetBool(int hkey) {
            bools.Remove(hkey);
        }

        public float GetFloat(string skey, int hkey, float def) {
            if (floats.TryGetValue(hkey, out var res)) {
                return res;
            }
            // cache miss
            res = source.GetFloat(skey, def);
            // write cache
            floats[hkey] = res;
            return res;
        }

        public void SetFloat(int hkey, float val) {
            floats[hkey] = val;
        }

        public void UnsetFloat(int hkey) {
            floats.Remove(hkey);
        }
        public int GetInt(string skey, int hkey, int def) {
            if (ints.TryGetValue(hkey, out var res)) {
                return res;
            }
            // cache miss
            res = source.GetInt(skey, def);
            // write cache
            ints[hkey] = res;
            return res;
        }

        public void SetInt(int hkey, int val) {
            ints[hkey] = val;
        }

        public void UnsetInt(int hkey) {
            ints.Remove(hkey);
        }
        public string GetString(string skey, int hkey, string def) {
            if (strings.TryGetValue(hkey, out var res)) {
                return res;
            }
            // cache miss
            res = source.GetString(skey, def);
            // write cache
            strings[hkey] = res;
            return res;
        }

        public void SetString(int hkey, string val) {
            strings[hkey] = val;
        }

        public void UnsetString(int hkey) {
            strings.Remove(hkey);
        }
    }
}
