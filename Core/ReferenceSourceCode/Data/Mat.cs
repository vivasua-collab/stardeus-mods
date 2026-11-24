using System;
using System.Collections.Generic;
using Game.Constants;
using Game.CodeGen;
using Game.Visuals;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    /// <summary>
    /// Instance of a material.
    /// <seealso cref="MatType"/>
    /// </summary>
    [Serializable] // due to DatoidCosts in Researchable
    [MessagePackObject]
    public struct Mat {
        public struct RarityComparer : IComparer<Mat> {
            public int Compare(Mat x, Mat y) {
                return (int) x.Type.Rarity - (int) y.Type.Rarity;
            }
        }
        [Key("TypeId")]
        public string TypeId;
        [IgnoreMember] public MatType Type;
        [Key("StackSize")]
        public int StackSize;
        [Key("MaxStackSize")]
        public int MaxStackSize;
        [IgnoreMember]
        public int Diff => MaxStackSize - StackSize;

        public static List<Mat> SerializeList(List<Mat> input, bool keepType = false) {
            if (Arrays.IsEmpty(input)) { return null; }
            var output = new List<Mat>(input.Count);
            for (int i = 0; i < input.Count; i++) {
                Mat mat = input[i];
                output.Add(mat.Serialize(keepType));
            }
            return output;
        }
        public static List<Mat> DeserializeList(List<Mat> input) {
            if (Arrays.IsEmpty(input)) { return null; }
            var output = new List<Mat>(input.Count);
            for (int i = 0; i < input.Count; i++) {
                Mat mat = input[i];
                if (mat.TryDeserialize(out var mat2)) {
                    output.Add(mat2);
                }
            }
            return output;
        }

        public Mat Serialize(bool keepType = false) {
            return new Mat() {
                Type = keepType ? Type : null,
                TypeId = Type.Id,
                StackSize = StackSize,
                MaxStackSize = MaxStackSize,
            };
        }

        public bool TryDeserialize(out Mat mat) {
            MatType type = null;
            if (Type?.Id != null) {
                D.Err("Trying to deserialize when mat type already exists!. {0} TypeId {1}",
                    Type.Id, TypeId);
                type = Type;
                this.TypeId = null;
            } else {
                #if !PRODUCTION_BUILD
                if (TypeId == "Steel") {
                    D.Err("Transforming Steel into SteelPlate");
                    TypeId = "SteelPlate";
                }
                #endif
                type = MatType.Get(TypeId);
            }
            mat = new Mat();
            mat.Type = type;
            mat.StackSize = StackSize;
            mat.MaxStackSize = Math.Max(StackSize, MaxStackSize);
            return type != null;
        }


        [IgnoreMember]
        public Mat Copy => new() {
            Type = Type,
            StackSize = StackSize,
            MaxStackSize = MaxStackSize,
        };

        public override string ToString() {
            if (MaxStackSize > 0) {
                return string.Format("{0}: {1}/{2}", Type?.Id ?? TypeId, StackSize, MaxStackSize);
            } else {
                return string.Format("{0} x{1}", Type?.Id ?? TypeId, StackSize);
            }
        }

        public override bool Equals(object obj) {
            return obj is Mat material &&
                   Type == material.Type &&
                   StackSize == material.StackSize &&
                   MaxStackSize == material.MaxStackSize;
        }

        public override int GetHashCode() {
            unchecked {
                return Type.GetHashCode() + 17 * StackSize + 121 * MaxStackSize;
            }
        }

        public static void PlayProvideVFX(GameState s, Vector2 from, Vector2 to, int amount) {
            if (!The.Prefs.GetBool(Pref.GCosmeticParticles, PrefH.GCosmeticParticles, true)) { return; }
            if (amount == 0) {
                D.Err("Trying to play provide mat VFX with 0 amount!");
                amount = 1;
            }
            var vfx = ParticlesVFX.Create("ProvideMatVFX", Consts.V3Zero)
                .WithoutOwnership();
            if (vfx.TryGetFX(out var fx)) {
                fx.SetFloat(ShaderVars.EmitRate, Mathf.Clamp(amount * 3f, 1f, 20f));
                fx.SetVector4(ShaderVars.Viewport, s.Grid.Bounds);
                fx.SetVector2(ShaderVars.From, EntityUtils.ClampToBounds(from));
                fx.SetVector2(ShaderVars.To, EntityUtils.ClampToBounds(to));
                var t = Time.timeSinceLevelLoad;
                fx.SetFloat(ShaderVars.EndTime, t + 1f);
            }
            vfx.SetClock(s.Clock);
            vfx.DestroyAfterRealTime(2f);
        }
    }
}
