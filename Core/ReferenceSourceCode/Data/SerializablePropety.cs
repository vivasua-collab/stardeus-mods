using System;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using MessagePack;

namespace Game.Data {
    [Serializable]
    [MessagePackObject]
    public class SerializableProperty {
        [Key("Key")]
        public string Key;
        private int keyH;
        [IgnoreMember]
        public int KeyH {
            get {
                if (keyH == 0) {
                    keyH = Hashes.S(Key);
                }
                return keyH;
            }
        }
        [Key("String")]
        public string String;
        [Key("Int")]
        public int Int;
        [Key("Float")]
        public float Float;
        [Key("Vector2")]
        public Vector2 Vector2;
        [Key("PosIdx")]
        public int PosIdx;
        [Key("Bool")]
        public bool Bool;
        [Key("BoolSet")]
        public bool[] BoolSet;
        [Key("IntSet")]
        public int[] IntSet;
        [Key("FloatSet")]
        public float[] FloatSet;
        [Key("StringSet")]
        public string[] StringSet;
        [Key("Vector2Set")]
        public Vector2[] Vector2Set;
        [Key("RawMaterials")]
        public Mat[] RawMaterials;
        [IgnoreMember]
        public bool IsUsed;

        public SerializableProperty MergeWith(SerializableProperty ovrd) {
            var newProp = new SerializableProperty();
            Copy(this, newProp);
            D.Ass(newProp.Key == ovrd.Key, "Copying {0} to {1}!!",
                newProp.Key, ovrd.Key);
            if (ovrd.String != null) { newProp.String = ovrd.String; }
            newProp.Int = ovrd.Int;
            newProp.Float = ovrd.Float;
            newProp.Bool = ovrd.Bool;
            newProp.PosIdx = ovrd.PosIdx;
            newProp.Vector2 = ovrd.Vector2;
            if (ovrd.BoolSet != null) { newProp.BoolSet = ovrd.BoolSet; }
            if (ovrd.IntSet != null) { newProp.IntSet = ovrd.IntSet; }
            if (ovrd.StringSet != null) { newProp.StringSet = ovrd.StringSet; }
            if (ovrd.FloatSet != null) { newProp.FloatSet = ovrd.FloatSet; }
            if (ovrd.Vector2Set != null) { newProp.Vector2Set = ovrd.Vector2Set; }
            if (ovrd.RawMaterials != null) { newProp.RawMaterials = ovrd.RawMaterials; }
            return newProp;
        }

        public static void Copy(SerializableProperty source, SerializableProperty target) {
            target.Key = source.Key;
            target.keyH = source.keyH;
            target.String = source.String;
            target.Int = source.Int;
            target.Float = source.Float;
            target.Vector2 = source.Vector2;
            target.PosIdx = source.PosIdx;
            target.Bool = source.Bool;
            target.BoolSet = source.BoolSet;
            target.IntSet = source.IntSet;
            target.StringSet = source.StringSet;
            target.FloatSet = source.FloatSet;
            target.Vector2Set = source.Vector2Set;
            target.RawMaterials = source.RawMaterials;
        }

        public override string ToString() {
            if (!string.IsNullOrWhiteSpace(String)) {
                return string.Format("{0}: {1} (String)", Key, String);
            }
            if (Int != 0) {
                return string.Format("{0}: {1} (Int)", Key, Int);
            }
            if (!Maths.Approximately(0f, Float)) {
                return string.Format("{0}: {1} (Float)", Key, Float);
            }
            if (PosIdx > 0) {
                return string.Format("{0}: {1} (Pos)", Key, PosIdx);
            }
            if (Vector2.x != 0 && Vector2.y != 0) {
                return string.Format("{0}: {1} (Vector2)", Key, Vector2);
            }
            if (BoolSet != null && BoolSet.Length > 0) {
                return string.Format("{0}: [Size:{1}] (BoolSet)", Key, BoolSet.Length);
            }
            if (IntSet != null && IntSet.Length > 0) {
                return string.Format("{0}: [Size:{1}] (IntSet)", Key, IntSet.Length);
            }
            if (FloatSet != null && FloatSet.Length > 0) {
                return string.Format("{0}: [Size:{1}] (FloatSet)", Key, FloatSet.Length);
            }
            if (StringSet != null && StringSet.Length > 0) {
                return string.Format("{0}: [Size:{1}] (StringSet)", Key, StringSet.Length);
            }
            if (Vector2Set != null && Vector2Set.Length > 0) {
                return string.Format("{0}: [Size:{1}] (Vector2Set)", Key, Vector2Set.Length);
            }
            if (RawMaterials != null && RawMaterials.Length > 0) {
                return string.Format("{0}: [Size:{1}] (RawMaterials)", Key, RawMaterials.Length);
            }
            return string.Format("{0}: {1} (Bool)", Key, Bool);
        }
    }
}
