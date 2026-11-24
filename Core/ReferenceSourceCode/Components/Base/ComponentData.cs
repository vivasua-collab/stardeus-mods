using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Components {
    [MessagePackObject]
    public class ComponentData {
        public enum DataMode {
            Any,
            Save,
            Load
        }
        [IgnoreMember] public bool IsEmpty {
            get {
                return
                    Floats == null &&
                    Ints == null &&
                    Strings == null &&
                    Longs == null &&
                    Bools == null &&
                    StringSets == null &&
                    IntSets == null &&
                    Mats == null &&
                    FloatSets == null;
            }
        }
        [IgnoreMember] public DataMode Mode = DataMode.Any;
        [IgnoreMember] public Entity Context;
        [Key(0)] public int EntityId;
        [Key(1)] public string Component;
        [Key(2)] public Dictionary<string, float> Floats;
        [Key(3)] public Dictionary<string, int> Ints;
        [Key(4)] public Dictionary<string, string> Strings;
        [Key(5)] public Dictionary<string, long> Longs;
        [Key(6)] public Dictionary<string, bool> Bools;
        [Key(7)] public Dictionary<string, string[]> StringSets;
        [Key(8)] public Dictionary<string, int[]> IntSets;
        [Key(9)] public Dictionary<string, Mat[]> Mats;
        [Key(10)] public Dictionary<string, float[]> FloatSets;
        public static ComponentData Create(string name) {
            return new ComponentData(0, name);
        }
        public ComponentData() { }
        public ComponentData(int entityId, string compName) {
            EntityId = entityId;
            Component = compName;
        }

        public bool TryGetString(string key, out string val) {
            val = null;
            if (Mode == DataMode.Save) {
                D.Err("Trying to TryGetString while saving! {0} {1}", Component, key);
            }
            if (Strings == null) { return false; }
            return Strings.TryGetValue(key, out val);
        }
        public bool TryGetUint(string key, out uint val) {
            val = 0;
            if (Mode == DataMode.Save) {
                D.Err("Trying to TryGetInt while saving! {0} {1}", Component, key);
            }
            if (Ints == null) { return false; }
            if (Ints.TryGetValue(key, out int intVal)) {
                val = (uint) intVal;
                return true;
            }
            val = 0;
            return false;

        }
        public bool TryGetLong(string key, out long val) {
            val = 0L;
            if (Mode == DataMode.Save) {
                D.Err("Trying to TryGetLong while saving! {0} {1}", Component, key);
            }
            if (Longs == null) { return false; }
            return Longs.TryGetValue(key, out val);
        }
        public bool TryGetBool(string key, out bool val) {
            val = false;
            if (Mode == DataMode.Save) {
                D.Err("Trying to TryGetBool while saving! {0} {1}", Component, key);
            }
            if (Bools == null) { return false; }
            return Bools.TryGetValue(key, out val);
        }
        public bool TryGetInt(string key, out int val) {
            val = 0;
            if (Mode == DataMode.Save) {
                D.Err("Trying to TryGetInt while saving! {0} {1}", Component, key);
            }
            if (Ints == null) { return false; }
            return Ints.TryGetValue(key, out val);
        }
        public bool TryGetFloat(string key, out float val) {
            val = 0f;
            if (Mode == DataMode.Save) {
                D.Err("Trying to TryGetFloat while saving! {0} {1}", Component, key);
            }
            if (Floats == null) { return false; }
            return Floats.TryGetValue(key, out val);
        }

        public float GetFloat(string key) {
            return GetFloat(key, 0f);
        }

        public float GetFloat(string key, float def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetFloat while saving! {0} {1}", Component, key);
            }
            if (Floats == null) { return def; }
            var fl = Floats.Get(key, def);
            if (!(fl > float.MinValue && fl < float.MaxValue)) {
                D.Err("Trying to get invalid float! Will return default {2}. {0} -> {1}. Context: {3}",
                    key, fl, def, Context);
                return def;
            }
            return fl;
        }
        public void RemoveFloat(string key) {
            Floats?.Remove(key);
        }

        public void SetFloat(string key, float value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetFloat while loading! {0} {1}", Component, key);
            }
            if (Floats == null) { Floats = new(); }
            if (!(value > float.MinValue && value < float.MaxValue)) {
                D.Err("Trying to set invalid float! Will set 0. {0} -> {1}. Context: {2}",
                    key, value, Context);
                return;
            }
            Floats[key] = value;
        }

        public uint GetUInt(string key) {
            return GetUInt(key, 0);
        }
        public int GetInt(string key) {
            return GetInt(key, 0);
        }
        public uint GetUInt(string key, uint def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetUInt while saving! {0} {1}", Component, key);
            }
            if (Ints == null) { return def; }
            return (uint) Ints.Get(key, (int) def);
        }
        public int GetInt(string key, int def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetInt while saving! {0} {1}", Component, key);
            }
            if (Ints == null) { return def; }
            return Ints.Get(key, def);
        }
        public void SetUInt(string key, uint value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetInt while loading! {0} {1}", Component, key);
            }
            if (Ints == null) { Ints = new(); }
            Ints[key] = (int) value;
        }
        public void SetInt(string key, int value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetInt while loading! {0} {1}", Component, key);
            }
            if (Ints == null) { Ints = new(); }
            Ints[key] = value;
        }
        public void RemoveBool(string key) {
            Bools?.Remove(key);
        }
        public void RemoveInt(string key) {
            Ints?.Remove(key);
        }
        public void RemoveIntSet(string key) {
            IntSets?.Remove(key);
        }
        public void RemoveStringSet(string key) {
            StringSets?.Remove(key);
        }
        public void RemoveFloatSet(string key) {
            FloatSets?.Remove(key);
        }
        public void RemoveLong(string key) {
            Longs?.Remove(key);
        }
        public bool TryGetVector2(string key, out Vector2 res) {
            if (TryGetFloat($"{key}.X", out var x)) {
                if (TryGetFloat($"{key}.Y", out var y)) {
                    res = new Vector2(x, y);
                    return true;
                }
            }
            res = default;
            return false;
        }
        //[Obsolete("Produces garbage on save / load")]
        public Vector2 GetVector2(string key) {
            var x = GetFloat($"{key}.X", 0);
            var y = GetFloat($"{key}.Y", 0);
            return new Vector2(x, y);
        }

        //[Obsolete("Produces garbage on save / load")]
        public Vector2 GetVector2(string key, Vector2 def) {
            var x = GetFloat($"{key}.X", def.x);
            var y = GetFloat($"{key}.Y", def.y);
            return new Vector2(x, y);
        }

        //[Obsolete("Produces garbage on save / load")]
        public void SetVector2(string key, Vector2 value) {
            SetFloat($"{key}.X", value.x);
            SetFloat($"{key}.Y", value.y);
        }
        public long GetLong(string key) {
            return GetLong(key, 0);
        }
        public long GetLong(string key, long def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetLong while saving! {0} {1}", Component, key);
            }
            if (Longs == null) { return def; }
            return Longs.Get(key, def);
        }
        public void SetLong(string key, long value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetLong while loading! {0} {1}", Component, key);
            }
            if (Longs == null) { Longs = new(); }
            Longs[key] = value;
        }
        public string GetString(string key) {
            return GetString(key, null);
        }
        public string GetString(string key, string def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetString while saving! {0} {1}", Component, key);
            }
            if (Strings == null) { return def; }
            return Strings.Get(key, def);
        }
        public void SetString(string key, string value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetString while loading! {0} {1}", Component, key);
            }
            if (value == null) {
                Strings?.Remove(key);
            } else {
                if (Strings == null) { Strings = new(); }
                Strings[key] = value;
            }
        }
        public Mat[] GetMats(string key, Mat[] def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetMats while saving! {0} {1}", Component, key);
            }
            if (Mats == null) { return def; }
            var mats =  Mats.Get(key, def);
            if (mats != null) {
                for (int i = 0; i < mats.Length; i++) {
                    Mat mat = mats[i];
                    if (mat.TryDeserialize(out var m)) {
                        mats[i] = m;
                    }
                }
            }
            return RemoveInvalid(mats);
        }

        private Mat[] RemoveInvalid(Mat[] mats) {
            if (mats == null) { return null; }
            bool hasInvalidMat = false;
            for (int i = 0; i < mats.Length; i++) {
                Mat mat = mats[i];
                if (mat.Type == null) {
                    hasInvalidMat = true;
                    break;
                }
            }
            if (!hasInvalidMat) { return mats; }

            var newMats = new List<Mat>(mats.Length - 1);
            for (int i = 0; i < mats.Length; i++) {
                Mat mat = mats[i];
                if (mat.Type != null) { newMats.Add(mat); }
            }
            return newMats.ToArray();
        }

        public void SetMats(string key, Mat[] value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetMats while loading! {0} {1}", Component, key);
            }
            if (value != null) {
                for (int i = 0; i < value.Length; i++) {
                    Mat mat = value[i];
                    mat.TypeId = mat.Type.Id;
                    value[i] = mat;
                }
            } else {
                Mats?.Remove(key);
                return;
            }
            if (Mats == null) { Mats = new(); }
            Mats[key] = value;
        }

        public void SetIntSet(string key, int[] value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetIntSet while loading! {0} {1}", Component, key);
            }
            if (value == null) {
                IntSets?.Remove(key);
                return;
            }
            if (IntSets == null) { IntSets = new(); }
            IntSets[key] = value;
        }

        public int[] GetIntSet(string key, int[] def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetIntSet while saving! {0} {1}", Component, key);
            }
            if (IntSets == null) { return def; }
            return IntSets.Get(key, def);
        }

        public float[] GetFloatSet(string key, float[] def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetFloatSet while saving! {0} {1}", Component, key);
            }
            if (FloatSets == null) { return def; }
            return FloatSets.Get(key, def);
        }

        public void SetFloatSet(string key, float[] value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetFloatSet while loading! {0} {1}", Component, key);
            }
            if (value == null) {
                FloatSets?.Remove(key);
            } else {
                if (FloatSets == null) { FloatSets = new(); }
                FloatSets[key] = value;
            }
        }

        public string[] GetStringSet(string key, string[] def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetStringSet while saving! {0} {1}", Component, key);
            }
            if (StringSets == null) { return def; }
            return StringSets.Get(key, def);
        }

        public void SetStringSet(string key, string[] value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetStringSet while loading! {0} {1}", Component, key);
            }
            if (value == null) {
                StringSets?.Remove(key);
            } else {
                if (StringSets == null) { StringSets = new(); }
                StringSets[key] = value;
            }
        }

        public void WillLoad() {
            Mode = DataMode.Load;
        }
        public void WillSave() {
            Mode = DataMode.Save;
        }

        public void UnsetMode() {
            Mode = DataMode.Any;
        }
        public void SetComponent(string key, string nameKey, IComponent comp) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetComponent while loading! {0} {1}", Component, key);
            }
            if (comp != null) {
                if (!comp.Entity.IsActive) {
                    D.Err("Trying to save inactive comp: {0}", comp);
                    Ints?.Remove(key);
                    Strings?.Remove(nameKey);
                } else {
                    SetInt(key, comp.EntityId);
                    SetString(nameKey, comp.GetName());
                }
            } else {
                Ints?.Remove(key);
                Strings?.Remove(nameKey);
            }
        }

        public void SetComponent(string key, IComponent comp) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetComponent while loading! {0} {1}", Component, key);
            }
            if (comp != null) {
                if (!comp.Entity.IsActive) {
                    D.Err("Trying to save inactive comp: {0}", comp);
                    Ints?.Remove(key);
                } else {
                    SetInt(key, comp.EntityId);
                }
            } else {
                Ints?.Remove(key);
            }
        }

        public bool GetBool(string key, bool def) {
            if (Mode == DataMode.Save) {
                D.Err("Trying to GetBool while saving! {0} {1}", Component, key);
            }
            if (Bools == null) { return def; }
            return Bools.Get(key, def);
        }

        public void SetBool(string key, bool value) {
            if (Mode == DataMode.Load) {
                D.Err("Trying to SetBool while loading! {0} {1}", Component, key);
            }
            if (Bools == null) { Bools = new(); }
            Bools[key] = value;
        }

        public override string ToString() {
            return string.Format("CompData[{0} -> {1}]", EntityId, Component);
        }
    }
}
