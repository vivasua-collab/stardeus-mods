using System;
using System.Collections.Generic;
using Game.CodeGen;
using KL.Utils;

namespace Game.Data {
    public sealed class CachedProperties {
        private Dictionary<int, float> propsFloat;
        private Dictionary<int, int> propsInt;
        private Dictionary<int, Curve> propsCurve;
        private Dictionary<int, string> propsString;
        private Dictionary<int, bool> propsBool;
        private Dictionary<int, string[]> propsStringSet;
        private Dictionary<int, float[]> propsFloatSet;
        private readonly SerializableProperty[] properties;
        public CachedProperties(SerializableProperty[] properties) {
            if (properties == null) { return; }
            this.properties = properties;
        }

        public bool Get(int key, bool def) {
            if (propsBool != null && propsBool.TryGetValue(key, out var res)) {
                return res;
            }
            res = def;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        res = prop.Bool;
                        break;
                    }
                }
            }
            propsBool ??= new();
            propsBool.Add(key, res);
            return res;
        }
        public int Get(int key, int def) {
            if (propsInt != null && propsInt.TryGetValue(key, out var res)) {
                return res;
            }
            res = def;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        res = prop.Int;
                        break;
                    }
                }
            }
            propsInt ??= new();
            propsInt.Add(key, res);
            return res;
        }
        public float Get(int key, float def) {
            if (propsFloat != null && propsFloat.TryGetValue(key, out var res)) {
                return res;
            }
            res = def;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        res = prop.Float;
                        break;
                    }
                }
            }
            propsFloat ??= new();
            propsFloat.Add(key, res);
            return res;
        }

        public bool TryString(int key, out string res) {
            if (propsString != null && propsString.TryGetValue(key, out res)) {
                return !string.IsNullOrEmpty(res);
            }
            res = null;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        if (prop.String == null) {
                            D.Err("String not defined on property: {0}", prop);
                        } else {
                            res = prop.String;
                        }
                        break;
                    }
                }
            }
            propsString ??= new();
            propsString.Add(key, res);
            return !string.IsNullOrEmpty(res);
        }
        public string Get(int key, string def) {
            if (propsString != null && propsString.TryGetValue(key, out var res)) {
                return res;
            }
            res = def;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        if (prop.String == null) {
                            D.Err("String not defined on property: {0}", prop);
                        } else {
                            res = prop.String;
                        }
                        break;
                    }
                }
            }
            propsString ??= new();
            propsString.Add(key, res);
            return res;
        }
        public bool TryFloatSet(int key, out float[] res) {
            if (propsFloatSet != null && propsFloatSet.TryGetValue(key, out res)) {
                return res != null && res.Length > 0;
            }
            res = null;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        if (prop.FloatSet == null) {
                            D.Err("FloatSet not defined on property: {0}", prop);
                        } else {
                            res = prop.FloatSet;
                        }
                        break;
                    }
                }
            }
            propsFloatSet ??= new();
            propsFloatSet.Add(key, res);
            return res != null && res.Length > 0;
        }

        public bool TryStringSet(int key, out string[] res) {
            if (propsStringSet != null && propsStringSet.TryGetValue(key, out res)) {
                return res != null && res.Length > 0;
            }
            res = null;
            if (properties != null) {
                foreach (var prop in properties) {
                    if (prop.KeyH == key) {
                        if (prop.StringSet == null) {
                            D.Err("StringSet not defined on property: {0}", prop);
                        } else {
                            res = prop.StringSet;
                        }
                        break;
                    }
                }
            }
            propsStringSet ??= new();
            propsStringSet.Add(key, res);
            return res != null && res.Length > 0;
        }

        public bool TryCurve(int key, out Curve curve) {
            curve = GetCurve(key, false);
            return curve != null;
        }

        public Curve GetCurve(int key, bool require = true) {
            Curve curve = null;
            if (propsCurve == null || !propsCurve.TryGetValue(key, out curve)) {
                if (properties != null) {
                    foreach (var prop in properties) {
                        if (prop.KeyH == key) {
                            if (prop.FloatSet == null) {
                                if (!string.IsNullOrEmpty(prop.String)) {
                                    // get curve def!
                                    curve = CurveDef.Get(prop.String);
                                } else {
                                    D.Err("Curve (FloatSet / String) not defined on property: {0}", prop);
                                }
                            } else {
                                curve = prop.FloatSet;
                            }
                            break;
                        }
                    }
                }
                propsCurve ??= new();
                propsCurve.Add(key, curve);
            }
            if (require && curve == null) {
                throw new Exception(string.Format(
                    "Curve does not exist for key! {0}",
                        PropertyIdH.ResolveName(key)));
            }
            return curve;
        }

    }
}
