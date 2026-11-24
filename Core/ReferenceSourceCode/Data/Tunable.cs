// #define DEBUG_TUNABLES
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    [Serializable]
    public sealed class Tunable {
        public SerializableProperty[] Floats;
        public SerializableProperty[] Ints;
        public SerializableProperty[] IntSets;
        public SerializableProperty[] FloatSets;
        public SerializableProperty[] StringSets;
        public SerializableProperty[] Bools;
        public SerializableProperty[] Strings;
        public static readonly HashSet<string> AllKeys = new();
        public static readonly HashSet<string> AllFloats = new();
        public static readonly HashSet<string> AllInts = new();
        public static readonly HashSet<string> AllIntSets = new();
        public static readonly HashSet<string> AllBools = new();
        public static readonly HashSet<string> AllStrings = new();
        public static readonly HashSet<string> AllStringSets = new();
        public static readonly HashSet<string> AllFloatSets = new();
        private static readonly Dictionary<int, bool> bools = new();
        private static readonly Dictionary<int, int> ints = new();
        private static readonly Dictionary<int, int[]> intSets = new();
        private static readonly Dictionary<int, float[]> floatSets = new();
        private static readonly Dictionary<int, string[]> stringSets = new();
        private static readonly Dictionary<int, float> floats = new();
        private static readonly Dictionary<int, string> strings = new();

        #if !PRODUCTION_BUILD
        private static readonly Dictionary<int, float> ofloats = new();
        private static readonly Dictionary<int, int> oints = new();
        public static void Override(int key, int value) {
            var keyStr = TunableKey.ResolveName(key);
            // save the original value
            oints[key] = value;
            if (oints.TryAdd(key, value)) {
                D.Err("Already have an override saved for key: {0}", keyStr);
            }
            var keyVar = KL.Utils.Strings.ToVariableName(keyStr);
            // set the override
            ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, value);
        }

        public static void Override(int key, float value) {
            var keyStr = TunableKey.ResolveName(key);
            // save the original value
            ofloats[key] = value;
            if (ofloats.TryAdd(key, value)) {
                D.Err("Already have an override saved for key: {0}", keyStr);
            }
            var keyVar = KL.Utils.Strings.ToVariableName(keyStr);
            // set the override
            ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, value);
        }
        public static void ClearOverrides() {
            foreach (var o in oints) {
                var keyStr = TunableKey.ResolveName(o.Key);
                var keyVar = KL.Utils.Strings.ToVariableName(keyStr);
                // unset the override
                ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, ints[o.Key]);
            }
            oints.Clear();

            foreach (var o in ofloats) {
                var keyStr = TunableKey.ResolveName(o.Key);
                var keyVar = KL.Utils.Strings.ToVariableName(keyStr);
                // unset the override
                ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, floats[o.Key]);
            }
            ofloats.Clear();
        }

        public static void ClearOverride(int key) {
            var keyVar = KL.Utils.Strings.ToVariableName(TunableKey.ResolveName(key));
            if (oints.Remove(key)) {
                ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, ints[key]);
            }
            if (ofloats.Remove(key)) {
                ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, floats[key]);
            }
        }
        #endif

        public static void Reset() {
            AllKeys.Clear();
            AllInts.Clear();
            AllIntSets.Clear();
            AllFloats.Clear();
            AllFloatSets.Clear();
            AllBools.Clear();
            AllStrings.Clear();
            AllStringSets.Clear();
            bools.Clear();
            ints.Clear();
            intSets.Clear();
            floatSets.Clear();
            stringSets.Clear();
            floats.Clear();
            strings.Clear();
            #if DEBUG_TUNABLES
            // D.Warn("Tunables: Clearing");
            #endif
        }

        // [Obsolete("Use TunableKey constants!")]
        public static bool Bool(string skey) {
            return Bool(Hashes.S(skey));
        }
        public static bool Bool(int skey) {
            if (bools.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable bool not found: {0}", skey);
            return false;
        }
        public static bool TryBool(int skey, out bool res) {
            return bools.TryGetValue(skey, out res);
        }
        // [Obsolete("Use TunableKey constants!")]
        public static void SetBool(string skey, bool val) {
            SetBool(Hashes.S(skey), val);
        }
        public static void SetBool(int skey, bool val) {
            bools[skey] = val;
            var keyVar = KL.Utils.Strings.ToVariableName(TunableKey.ResolveName(skey));
            // set the override
            ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, val);
        }
        // [Obsolete("Use TunableKey constants!")]
        public static bool TryString(string key, out string res) {
            return TryString(Hashes.S(key), out res);
        }
        public static bool TryString(int skey, out string res) {
            return strings.TryGetValue(skey, out res);
        }
        //[Obsolete("Use TunableKey constants!")]
        public static bool TryInt(string key, out int res) {
            return TryInt(Hashes.S(key), out res);
        }
        public static bool TryInt(int skey, out int res) {
            return ints.TryGetValue(skey, out res);
        }
        // [Obsolete("Use TunableKey constants!")]
        public static int Int(string key) {
            return Int(Hashes.S(key));
        }
        public static int Int(int skey) {
            #if !PRODUCTION_BUILD
            if (oints.TryGetValue(skey, out var ores)) {
                return ores;
            }
            #endif
            if (ints.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable int not found: {0}", skey);
            return 0;
        }
        // [Obsolete("Use TunableKey constants!")]
        public static void SetInt(string skey, int val) {
            SetInt(Hashes.S(skey), val);
        }
        public static void SetInt(int skey, int val) {
            ints[skey] = val;
            var keyVar = KL.Utils.Strings.ToVariableName(TunableKey.ResolveName(skey));
            // set the override
            ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, val);
        }
        // [Obsolete("Use TunableKey constants!")]
        public static int[] IntSet(string skey) {
            return IntSet(Hashes.S(skey));
        }
        public static int[] IntSet(int skey) {
            if (intSets.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable int set not found: {0}", skey);
            return null;
        }
        // [Obsolete("Use TunableKey constants!")]
        public static float[] FloatSet(string skey) {
            return FloatSet(Hashes.S(skey));
        }
        public static bool TryFloatSet(string skey, out float[] floats) {
            var ikey = Hashes.S(skey);
            if (floatSets.TryGetValue(ikey, out var res)) {
                floats = res;
                return true;
            }
            floats = null;
            return false;
        }
        public static float[] FloatSet(int skey) {
            if (floatSets.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable float set not found: {0}", skey);
            return null;
        }
        // [Obsolete("Use TunableKey constants!")]
        public static string[] StringSet(string skey) {
            return StringSet(Hashes.S(skey));
        }
        // ReSharper disable Unity.PerformanceAnalysis
        public static string[] StringSet(int skey) {
            if (stringSets.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable string set not found: {0}", skey);
            return null;
        }
        public static bool TryStringSet(string key, out string[] res) {
            if (stringSets.TryGetValue(Hashes.S(key), out res)) {
                return true;
            }
            res = null;
            return false;
        }
        // [Obsolete("Use TunableKey constants!")]
        public static float Float(string key) {
            return Float(Hashes.S(key));
        }
        // ReSharper disable Unity.PerformanceAnalysis
        public static float FloatDifficultyAdj(int skey, float diff) {
            return Float(skey) * diff;
        }
        // [Obsolete("Use Tunables.VarName")]
        public static float Float(int skey) {
            #if !PRODUCTION_BUILD
            if (ofloats.TryGetValue(skey, out var ores)) {
                return ores;
            }
            #endif
            if (floats.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable float not found: {0}", skey);
            return 0f;
        }
        // [Obsolete("Use TunableKey constants!")]
        public static void SetFloat(string skey, float val) {
            SetFloat(Hashes.S(skey), val);
        }
        public static void SetFloat(int skey, float val) {
            floats[skey] = val;
            var keyVar = KL.Utils.Strings.ToVariableName(TunableKey.ResolveName(skey));
            // set the override
            ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, val);
        }
        // [Obsolete("Use TunableKey constants!")]
        public static string String(string skey) {
            return String(Hashes.S(skey));
        }
        public static string String(int skey) {
            if (strings.TryGetValue(skey, out var res)) {
                return res;
            }
            D.Err("Tunable string not found: {0}", skey);
            return null;
        }
        // [Obsolete("Use TunableKey constants!")]
        public static void SetString(string skey, string val) {
            SetString(Hashes.S(skey), val);
        }
        public static void SetString(int skey, string val) {
            strings[skey] = val;
            var keyVar = KL.Utils.Strings.ToVariableName(TunableKey.ResolveName(skey));
            // set the override
            ReflectionUtils.SetPublicStaticField(typeof(Tunables), keyVar, val);
        }

        public static bool HasVariable(string key) {
            if (key.Contains(".")) { return false; }
            if (key.Contains("/")) { return false; }
            if (key.Contains(":")) { return false; }
            return true;
        }

        public static void Load(string path, Tunable tunable) {
            #if DEBUG_TUNABLES
            // D.Warn("Tunables: Loading {0}", path);
            #endif
            if (!Arrays.IsEmpty(tunable.Floats)) {
                foreach (var v in tunable.Floats) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading Float {0} -> {1}", v.Key, v.Float);
                    #endif
                    AllKeys.Add(v.Key);
                    AllFloats.Add(v.Key);
                    floats[v.KeyH] = v.Float;
                    float val = v.Float;
                    #if !PRODUCTION_BUILD
                    // check if there is an override
                    if (ofloats.TryGetValue(v.KeyH, out var ov)) {
                        val = ov;
                    }
                    #endif
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), val);
                    }
                }
            }
            if (!Arrays.IsEmpty(tunable.Ints)) {
                foreach (var v in tunable.Ints) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading Int {0} -> {1}", v.Key, v.Int);
                    #endif
                    AllKeys.Add(v.Key);
                    AllInts.Add(v.Key);
                    var val = v.Int;
                    ints[Hashes.S(v.Key)] = val;
                    #if !PRODUCTION_BUILD
                    // check if there is an override
                    if (oints.TryGetValue(v.KeyH, out var ov)) {
                        val = ov;
                    }
                    #endif
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), val);
                    }
                }
            }
            if (!Arrays.IsEmpty(tunable.Strings)) {
                foreach (var v in tunable.Strings) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading String {0} -> {1}", v.Key, v.String);
                    #endif
                    AllKeys.Add(v.Key);
                    AllStrings.Add(v.Key);
                    strings[Hashes.S(v.Key)] = v.String;
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), v.String);
                    }
                }
            }
            if (!Arrays.IsEmpty(tunable.Bools)) {
                foreach (var v in tunable.Bools) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading Bool {0} -> {1}", v.Key, v.Bool);
                    #endif
                    AllKeys.Add(v.Key);
                    AllBools.Add(v.Key);
                    bools[Hashes.S(v.Key)] = v.Bool;
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), v.Bool);
                    }
                }
            }
            if (!Arrays.IsEmpty(tunable.IntSets)) {
                foreach (var v in tunable.IntSets) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading IntSet {0} -> {1}", v.Key, v.IntSet);
                    #endif
                    AllKeys.Add(v.Key);
                    AllIntSets.Add(v.Key);
                    intSets[Hashes.S(v.Key)] = v.IntSet;
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), v.IntSet);
                    }
                }
            }
            if (!Arrays.IsEmpty(tunable.FloatSets)) {
                foreach (var v in tunable.FloatSets) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading FloatSet {0} -> {1}", v.Key, v.FloatSet);
                    #endif
                    AllKeys.Add(v.Key);
                    AllFloatSets.Add(v.Key);
                    floatSets[Hashes.S(v.Key)] = v.FloatSet;
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), v.FloatSet);
                    }
                }
            }
            if (!Arrays.IsEmpty(tunable.StringSets)) {
                foreach (var v in tunable.StringSets) {
                    #if DEBUG_TUNABLES
                    // D.Warn("Tunables: Loading StringSet {0} -> {1}", v.Key, v.StringSet);
                    #endif
                    AllKeys.Add(v.Key);
                    AllStringSets.Add(v.Key);
                    stringSets[Hashes.S(v.Key)] = v.StringSet;
                    if (HasVariable(v.Key)) {
                        ReflectionUtils.SetPublicStaticField(typeof(Tunables),
                            KL.Utils.Strings.ToVariableName(v.Key), v.StringSet);
                    }
                }
            }
        }
    }
}
