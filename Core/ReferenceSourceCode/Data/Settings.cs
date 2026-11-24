using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Components;
using Game.Constants;
using KL.Utils;
using System.Reflection;
using System;

namespace Game.Data {
    public sealed class Settings {
        public ComponentData Data;

        public CachedComponentData Cache;
        private readonly bool isGlobal;
        private readonly char[] separator = new [] { ':' };
        private HashSet<string> allNames;
        private readonly HashSet<string> privateNames = new() {
            "cam.memory"
        };

        // TODO handle on the fly keys (except bools)
        public HashSet<string> AllNames {
            get {
                if (allNames == null) {
                    // Handle declared names
                    allNames = new();
                    var consts = typeof(Pref).GetFields(
                        BindingFlags.Public | BindingFlags.Static);
                    foreach (var c in consts) {
                        var n = c.Name;
                        var isThisGlobal = n.StartsWith("Global");
                        if (isGlobal == isThisGlobal) {
                            allNames.Add(c.GetValue(null).ToString());
                        }
                    }
                }
                return allNames;
            }
        }

        public HashSet<string> PublicNames {
            get {
                var hs = new HashSet<string>();
                foreach (var name in AllNames) {
                    if (privateNames.Contains(name)) { continue; }
                    var ok = true;
                    foreach (var priv in privateNames) {
                        if (name.StartsWith(priv)) {
                            ok = false;
                            break;
                        }
                    }
                    if (ok) {
                        hs.Add(name);
                    }
                }
                return hs;
            }
        }

        public void SetData(ComponentData data) {
            Data = data;
            Cache = new CachedComponentData(data);
            allNames = null;
        }

        public Settings(bool isGlobal) {
            this.isGlobal = isGlobal;
            Reset();
        }

        public void Reset() {
            var name = isGlobal ? "setting.global" : "settings.game";
            Data = new ComponentData(0, name);
            Cache = new CachedComponentData(Data);
            allNames = null;
        }

        public void SetBool(string key, bool val) {
            // D.Warn("Setting pref: {0}: {1}", key, val);
            Data.SetBool(key, val);
            Cache.SetBool(Hashes.S(key), val);
            var names = AllNames;
            if (!names.Contains(key)) { names.Add(key); }
            The.SysSig.SettingChanged.Send(key);
        }

        [Obsolete("Use cached version")]
        public bool GetBool(string key, bool def) {
            return Cache.GetBool(key, Hashes.S(key), def);
        }

        public bool GetBoolAutoHash(string key, bool def) {
            return Cache.GetBool(key, Hashes.S(key), def);
        }

        public bool GetBool(string skey, int hkey, bool def) {
            #if !PRODUCTION_BUILD
            if (Hashes.S(skey) != hkey) {
                D.Err("Incorrect cached GetBool! {0}", skey);
            }
            #endif
            return Cache.GetBool(skey, hkey, def);
        }

        // Matches against the stored data, not AllNames: AllNames only knows the
        // Pref constants and runtime bools, so on the fly string / int keys
        // (input binding overrides) were never cleared through it.
        public void ClearMatching(string v) {
            var toRemove = new List<string>();
            CollectMatching(Data.Bools, v, toRemove);
            CollectMatching(Data.Strings, v, toRemove);
            CollectMatching(Data.Ints, v, toRemove);
            CollectMatching(Data.Floats, v, toRemove);
            for (int i = 0; i < toRemove.Count; i++) {
                string n = toRemove[i];
                Data.Bools?.Remove(n);
                Data.Strings?.Remove(n);
                Data.Ints?.Remove(n);
                Data.Floats?.Remove(n);
                var h = Hashes.S(n);
                Cache.UnsetBool(h);
                Cache.UnsetString(h);
                Cache.UnsetInt(h);
                Cache.UnsetFloat(h);
                allNames?.Remove(n);
            }
        }

        private static void CollectMatching<T>(Dictionary<string, T> values, string start,
                List<string> into) {
            if (values == null) { return; }
            foreach (var key in values.Keys) {
                if (key.StartsWith(start, StringComparison.Ordinal) && !into.Contains(key)) {
                    into.Add(key);
                }
            }
        }

        public void ClearMatchingBools(string v) {
            var toRemove = new List<string>();
            foreach (var n in AllNames) {
                if (n.StartsWith(v)) {
                    toRemove.Add(n);
                    Data.Bools.Remove(n);
                    Cache.UnsetBool(Hashes.S(n));
                }
            }
            for (int i = 0; i < toRemove.Count; i++) {
                string r = toRemove[i];
                allNames.Remove(r);
            }
        }

        public void RegisterBool(string key, bool def) {
            var val = Data.GetBool(key, def);
            Data.SetBool(key, val);
            Cache.SetBool(Hashes.S(key), val);
        }

        public void SetInt(string key, int val) {
            Data.SetInt(key, val);
            Cache.SetInt(Hashes.S(key), val);
            The.SysSig.SettingChanged.Send(key);
        }
        public void UnsetInt(string key) {
            Data.Ints?.Remove(key);
            Cache.UnsetInt(Hashes.S(key));
        }
        public void UnsetBool(string key) {
            Data.Bools?.Remove(key);
            Cache.UnsetBool(Hashes.S(key));
        }
        public void UnsetFloat(string key) {
            Data.Floats?.Remove(key);
            Cache.UnsetFloat(Hashes.S(key));
        }

        [Obsolete("Use cached version")]
        public int GetInt(string key, int def) {
            return Cache.GetInt(key, Hashes.S(key), def);
        }
        public int GetIntAutoHash(string key, int def) {
            return Cache.GetInt(key, Hashes.S(key), def);
        }
        public int GetInt(string skey, int hkey, int def) {
            #if !PRODUCTION_BUILD
            if (Hashes.S(skey) != hkey) {
                D.Err("Incorrect cached GetInt! {0}", skey);
            }
            #endif
            return Cache.GetInt(skey, hkey, def);
        }
        public int GetIntNonZero(string skey, int hkey, int def, int zeroVal) {
            var res = GetInt(skey, hkey, def);
            if (res == 0) {
                return zeroVal;
            }
            return res;
        }
        public void RegisterInt(string key, int def) {
            var val = Data.GetInt(key, def);
            Data.SetInt(key, val);
            Cache.SetInt(Hashes.S(key), val);
        }

        public void SetFloat(string key, float val) {
            Data.SetFloat(key, val);
            Cache.SetFloat(Hashes.S(key), val);
            The.SysSig.SettingChanged.Send(key);
        }

        [Obsolete("Use cached version")]
        public float GetFloat(string key, float def) {
            return Cache.GetFloat(key, Hashes.S(key), def);
        }
        public float GetFloatAutoHash(string key, float def) {
            return Cache.GetFloat(key, Hashes.S(key), def);
        }
        public float GetFloat(string skey, int hkey, float def) {
            #if !PRODUCTION_BUILD
            if (Hashes.S(skey) != hkey) {
                D.Err("Incorrect cached GetFloat! {0}", skey);
            }
            #endif
            return Cache.GetFloat(skey, hkey, def);
        }
        public float GetFloatNonZero(string skey, int hkey, float def) {
            var res = GetFloat(skey, hkey, def);
            if (res == 0f) {
                SetFloat(skey, def);
                return def;
            }
            return res;
        }
        public float GetFloatNonZero(string skey, int hkey, float def, float zeroVal) {
            var res = GetFloat(skey, hkey, def);
            if (res == 0) {
                return zeroVal;
            }
            return res;
        }
        public void RegisterFloat(string key, float def) {
            float val = Data.GetFloat(key, def);
            Data.SetFloat(key, val);
            Cache.SetFloat(Hashes.S(key), val);
        }
        public void SetString(string key, string val) {
            Data.SetString(key, val);
            Cache.SetString(Hashes.S(key), val);
            The.SysSig.SettingChanged.Send(key);
        }

        [Obsolete("Use cached version")]
        public string GetString(string key, string def) {
            return Cache.GetString(key, Hashes.S(key), def);
        }
        public string GetStringAutoHash(string key, string def) {
            return Cache.GetString(key, Hashes.S(key), def);
        }
        public string GetString(string skey, int hkey, string def) {
            #if !PRODUCTION_BUILD
            if (Hashes.S(skey) != hkey) {
                D.Err("Incorrect cached GetString! {0}", skey);
            }
            #endif
            return Cache.GetString(skey, hkey, def);
        }
        public void RegisterString(string key, string def) {
            var val = Data.GetString(key, def);
            Data.SetString(key, val);
            Cache.SetString(Hashes.S(key), val);
        }

        public bool Find(string name, out string type, out object value) {
            if (Data.Bools != null && Data.Bools.TryGetValue(name, out var b)) {
                type = "bool";
                value = b;
                return true;
            }
            if (Data.Strings != null && Data.Strings.TryGetValue(name, out var s)) {
                type = "string";
                value = s;
                return true;
            }
            if (Data.Floats != null && Data.Floats.TryGetValue(name, out var f)) {
                type = "float";
                value = f;
                return true;
            }
            if (Data.Ints != null && Data.Ints.TryGetValue(name, out var i)) {
                type = "int";
                value = i;
                return true;
            }
            // TODO handle IntSets and StringSets
            type = "null";
            value = null;
            return false;
        }

        public string SerializeToString() {
            var allSettings = new Dictionary<string, string>();
            var allSettingTypes = new Dictionary<string, string>();
            if (Data.Bools != null) {
                foreach (var b in Data.Bools) {
                    allSettings[b.Key] = b.Value.ToString();
                    allSettingTypes[b.Key] = "bool";
                }
            }
            if (Data.Strings != null) {
                foreach (var b in Data.Strings) {
                    if (string.IsNullOrEmpty(b.Value)) { continue; }
                    allSettings[b.Key] = b.Value;
                    allSettingTypes[b.Key] = "string";
                }
            }
            if (Data.Ints != null) {
                foreach (var b in Data.Ints) {
                    allSettings[b.Key] = b.Value.ToString();
                    allSettingTypes[b.Key] = "int";
                }
            }
            if (Data.Floats != null) {
                foreach (var b in Data.Floats) {
                    allSettings[b.Key] = b.Value.ToString();
                    allSettingTypes[b.Key] = "float";
                }
            }
            if (allSettings.Count == 0) { return ""; }
            var sb = new StringBuilder();
            foreach (var set in allSettings.OrderBy(s => s.Key)) {
                var type = allSettingTypes[set.Key];
                sb.AppendFormat("{0}:{1}:{2}\n", set.Key, type, set.Value);
            }
            return sb.ToString();
        }

        public void DeserializeFromString(string str) {
            if (string.IsNullOrWhiteSpace(str)) { return; }
            foreach (var line in str.Split('\n')) {
                try {
                    if (string.IsNullOrWhiteSpace(line)) { continue; }
                    var s = line.Trim().Split(separator, 3);
                    if (s.Length < 3) {
                        D.Err("Invalid setting: {0}", line);
                        continue;
                    }
                    var key = s[0];
                    var type = s[1];
                    var val = s[2];
                    //// D.Warn("Overloading setting from file: {0} -> {1}", key, val);
                    switch (type) {
                        case "bool": SetBool(key, Convert.ToBoolean(val)); break;
                        case "int": SetInt(key, Convert.ToInt32(val)); break;
                        case "float": SetFloat(key, Convert.ToSingle(val)); break;
                        case "string": SetString(key, val); break;
                        default: D.Err("Unknown setting type: {0}", line); break;
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed deserializing setting line: {0}", line);
                }
            }
        }
    }
}
