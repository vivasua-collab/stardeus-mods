using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Linq;
using Game.Systems.UIUX;

namespace Game.Components {
    // TODO NEXT TIME YOU TOUCH THIS - refactor
    // fucking hell I'm not touching this!
    [Serializable]
    public class ComponentConfig {
        //[Obsolete("Use ComponentH")]
        /// <summary> WARNING: use ComponentH when possible</summary>
        public string Component;
        private int componentH;
        public int ComponentH {
            get {
                if (componentH == 0) {
                    componentH = Hashes.S(Component);
                }
                return componentH;
            }
        }
        public SerializableProperty[] Properties;
        private Dictionary<int, SerializableProperty> hashedProperties;

        public static ComponentConfig For(SerializableProperty[] props) {
            var cfg = new ComponentConfig();
            cfg.Properties = props;
            return cfg;
        }

        public ComponentConfig Copy() {
            var cc = new ComponentConfig();
            cc.Component = Component;
            cc.componentH = componentH;
            cc.Properties = MergeWith(Properties, null);
            return cc;
        }

        public override string ToString() {
            return $"CompCfg[{Component}]";
        }

        private static SerializableProperty[] MergeWith(SerializableProperty[] our,
                SerializableProperty[] their) {
            if (our != null) {
                // Prevent overwriting values in the original root def
                var our2 = new SerializableProperty[our.Length];
                Array.Copy(our, our2, our.Length);
                our = our2;
            }

            // See if there are other values defined
            if (their == null || their.Length == 0) { return our; }

            // Prevent overwriting values in the original root def
            var their2 = new SerializableProperty[their.Length];
            Array.Copy(their, their2, their.Length);
            their = their2;

            // Maybe we didn't have values ourselves?
            if (our == null || our.Length == 0) { return their2; }
            var newValues = new List<SerializableProperty>();


            int ourSize = 0;
            // Sadly we did. Let's loop though new values
            for (int i1 = 0; i1 < their.Length; i1++) {
                SerializableProperty theirItem = their[i1];
                ourSize = our.Length;
                bool isNew = true;
                // Check if we already had a key for this crap
                for (int i = 0; i < our.Length; i++) {
                    SerializableProperty ownItem = our[i];
                    // Damn, we had
                    if (ownItem.Key == theirItem.Key) {
                        isNew = false;
                        // Rewrite our old value
                        our[i] = ownItem.MergeWith(theirItem);
                        break;
                    }
                }
                if (isNew) {
                    // We didn't have a key, let's remember this new crap
                    newValues.Add(theirItem);
                }
            }

            // We had some new keys that did not exist before
            if (newValues.Count == 0) { return our; }

            if (ourSize > 0) {
                // merge new and old values
                foreach (var ourItem in our) {
                    newValues.Add(ourItem);
                }
            }

            return newValues.ToArray();
        }


        public void MergeWith(ComponentConfig other) {
            Properties = MergeWith(Properties, other.Properties);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Obsolete("Remove", false)]
        public bool TryFindProperty(string key, out SerializableProperty property) {
            var keyH = Hashes.S(key);
            return TryFindProperty(keyH, out property);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryFindProperty(int keyH, out SerializableProperty property) {
            if (Properties == null) {
                property = null;
                return false;
            }
            if (hashedProperties == null) {
                hashedProperties = new Dictionary<int, SerializableProperty>();
                property = null;
                for (int i = 0; i < Properties.Length; i++) {
                    SerializableProperty prop = Properties[i];
                    if (prop.KeyH == keyH) {
                        prop.IsUsed = true;
                        property = prop;
                    }
                    if (!hashedProperties.TryAdd(prop.KeyH, prop)) {
                        D.Err("Duplicate property in component config: {0}->{1}",
                            this, prop.Key);
                    }
                }
                return property != null;
            }
            if (hashedProperties.TryGetValue(keyH, out property)) {
                property.IsUsed = true;
                return true;
            }
            return false;
        }

        public void SetProperty(SerializableProperty prop) {
            var key = prop.Key;
            for (int i = Properties.Length - 1; i >= 0; i--) {
                SerializableProperty f = Properties[i];
                if (f.Key == key) {
                    // Overwrite existing one
                    Properties[i] = prop;
                    if (hashedProperties != null) {
                        hashedProperties[prop.KeyH] = prop;
                    }
                    return;
                }
            }
            AddProperty(prop);
        }

        public float GetFloat(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Float;
            }
            NotifyMissingKey(key, "Float");
            return 0;
        }
        public float GetFloat(int key, float def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Float;
            }
            return def;
        }
        public Vector2 GetVector2(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Vector2;
            }
            NotifyMissingKey(key, "Vector2");
            return default;
        }

        public Vector2 GetVector2(int key, Vector2 def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Vector2;
            }
            return def;
        }
        public string GetString(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.String;
            }
            NotifyMissingKey(key, "String");
            return null;
        }

        public void AddProperty(SerializableProperty prop) {
            var pl = Properties.Length;
            Array.Resize(ref Properties, pl + 1);
            Properties[pl] = prop;
            hashedProperties = null;
        }

        public string GetString(int key, string def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.String;
            }
            return def;
        }
        public string[] GetStringSet(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.StringSet;
            }
            NotifyMissingKey(key, "StringSet");
            return null;
        }
        public string[] GetStringSet(int key, string[] def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.StringSet;
            }
            return def;
        }
        public int[] GetIntSet(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.IntSet;
            }
            NotifyMissingKey(key, "IntSet");
            return null;
        }
        public int[] GetIntSet(int key, int[] def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.IntSet;
            }
            return def;
        }
        ///<summary> Gets a copy of materials, if we ever modify it</summary>
        public Mat[] GetRawMaterialsCopy(int key, bool empty, Mat[] def) {
            if (TryFindProperty(key, out var prop)) {
                return Deserialized(prop.RawMaterials, empty, true);
            }
            return def;
        }

        /// <summary> Gets a ref of materials, if we don't need to modify</summary>
        public Mat[] GetRawMaterialsRef(int key, bool empty, Mat[] def) {
            if (TryFindProperty(key, out var prop)) {
                return Deserialized(prop.RawMaterials, empty, false);
            }
            return def;
        }

        private Mat[] Deserialized(Mat[] mats, bool empty, bool copy) {
            Mat[] res;
            if (copy) {
                res = new Mat[mats.Length];
            } else {
                res = mats;
            }
            bool hasNulls = false;
            for (int i = mats.Length - 1; i >= 0; i--) {
                var m = mats[i];
                if (m.TypeId != null) {
                    m.Type = MatType.Get(m.TypeId);
                    m.TypeId = null;
                    if (m.StackSize > m.MaxStackSize) {
                        m.MaxStackSize = m.StackSize;
                    }
                    if (empty) {
                        m.StackSize = 0;
                    }
                    // Rewrite deserialized once
                    mats[i] = m;
                }
                if (copy) {
                    if (empty) {
                        m.StackSize = 0;
                    }
                    res[i] = m;
                }
                if (m.Type == null) {
                    D.Err("Invalid material in component config: {0} -> {1}",
                        Component, m.TypeId);
                    hasNulls = true;
                }
            }
            if (hasNulls) {
                return res.Where(m => m.Type != null).ToArray();
            }
            return res;
        }

        public float[] GetFloatSet(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.FloatSet;
            }
            NotifyMissingKey(key, "FloatSet");
            return null;
        }
        public float[] GetFloatSet(int key, float[] def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.FloatSet;
            }
            return def;
        }
        public bool[] GetBoolSet(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.BoolSet;
            }
            NotifyMissingKey(key, "BoolSet");
            return null;
        }
        public bool[] GetBoolSet(int key, bool[] def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.BoolSet;
            }
            return def;
        }
        public Vector2[] GetVector2Set(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Vector2Set;
            }
            NotifyMissingKey(key, "Vector2Set");
            return null;
        }
        public Vector2[] GetVector2Set(int key, Vector2[] def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Vector2Set;
            }
            return def;
        }

        public bool GetBool(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Bool;
            }
            NotifyMissingKey(key, "Bool");
            return false;
        }

        public bool HasKey(int key) {
            return TryFindProperty(key, out _);
        }

        public bool GetBool(int key, bool def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Bool;
            }
            return def;
        }

        public int GetInt(int key) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Int;
            }
            NotifyMissingKey(key, "Int");
            return 0;
        }
        public int GetInt(int key, int def) {
            if (TryFindProperty(key, out var prop)) {
                return prop.Int;
            }
            return def;
        }

        public void MarkUsed(int key) {
            if (TryFindProperty(key, out var prop)) {
                prop.IsUsed = true;
            }
        }

        public void CheckUsage(Def owner) {
            if (Properties == null) { return; }
            for (int i = Properties.Length - 1; i >= 0; i--) {
                SerializableProperty p = Properties[i];
                if (p.IsUsed) { continue; }
                var fix = $"To fix it, remove the unused parameter from JSON or check if Config.Get[PROPERTY_TYPE](PropertyIdH.[PROPERTY_NAME]) in {Component}Comp.OnConfig() matches the property name and type ({p}) which is defined in the JSON.";
                if (ModRegistry.WarnAboutBrokenMod(owner,
                        $"Configuration parameter <b>{p}</b> is declared in JSON's <b>{Component}</b> block, but is not used in C# <b>{Component}Comp.OnConfig()</b>.<br><br>{Texts.GreySmaller(fix)}<br><br>")) {
                    D.Err("Property not used in component: {0} -> {1} -> {2}",
                        owner, Component, p);
                }
            }
        }

        private void NotifyMissingKey(int key, string type) {
            var name = PropertyIdH.ResolveName(key);
            string msg;
            if (name == null) {
                msg = string.Format(
                    "Component config for {0} is trying to get a null key for type {1}!",
                        Component, type);
            } else {
                msg = string.Format(
                    "Component config for {0} missing {1} key: {2}. All: {3}",
                        Component, type, name, D.Dump(Properties));
            }
            Ready.WhenCore(() => {
                WPopup.SystemWarning(T.Error, "Possible Game Breaking Bug", msg);
            });
            D.Err(msg);
        }
    }
}
