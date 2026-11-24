using System;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Creatures {
    [MessagePackObject]
    public class LookPart {
        // TypeId/HidesIds are the authoritative, serialized fields. Type/HidesMask are
        // resolved-once caches derived from them (see ResolvePostLoad). The live BeingLooks
        // is serialized directly (BeingDataData.Looks), so every writer of Type/Hides MUST
        // go through SetType/SetHides so the string fields never go stale.
        [Key("TypeId")] public string TypeId;
        [Key("SpriteId")] public string SpriteId;
        [Key("SubSprites")] public string[] SubSprites;
        [Key("PosFilter")] public string PosFilter;
        [Key("Color")] public Color Color = Consts.ColorWhite;
        [Key("HidesIds")] public string[] HidesIds;
        [SkipDumpInstance]
        [IgnoreMember] public int PosFilterH;
        [IgnoreMember] public Vector2 Offset;
        [IgnoreMember] public float Scale;
        [IgnoreMember] public int Index;
        [IgnoreMember] public bool RenderSorted;

        [IgnoreMember] public LookPartTypeDef Type;
        [IgnoreMember] public ulong HidesMask;

        // Override for replaced eyes
        [IgnoreMember] public bool IsFace => Type?.IsFace == true;

        public void SetType(LookPartTypeDef type) {
            Type = type;
            TypeId = type?.Id;
        }

        public void SetHides(string[] hidesIds, ulong hidesMask) {
            HidesIds = hidesIds;
            HidesMask = hidesMask;
        }

        public void ClearHides() {
            HidesIds = null;
            HidesMask = 0UL;
        }

        /// <summary>
        /// Resolves Type/HidesMask caches from TypeId/HidesIds after MessagePack deserialize.
        /// Must be called once on every LookPart coming straight out of a save file before it
        /// is used (matched, hidden-mask-tested, etc). Live (non-deserialized) parts are always
        /// fully resolved already via SetType/SetHides.
        /// </summary>
        public void ResolvePostLoad() {
            if (TypeId != null) {
                Type = LookPartTypeDef.Get(TypeId);
            }
            if (HidesIds == null || HidesIds.Length == 0) { return; }
            ulong mask = 0UL;
            for (int i = HidesIds.Length - 1; i >= 0; i--) {
                var hidden = LookPartTypeDef.Get(HidesIds[i]);
                if (hidden != null) {
                    mask |= hidden.Bit;
                }
            }
            HidesMask = mask;
        }

        public bool CopyLookFrom(LookPart part) {
            if (Type != part.Type) {
                D.Err("Trying to copy {0} in to {1}!", this, part);
                return false;
            }
            bool changed = false;
            if (SpriteId != part.SpriteId) {
                SpriteId = part.SpriteId;
                changed = true;
            }
            if (SubSprites == null != (part.SubSprites == null)) {
                SubSprites = part.SubSprites;
                changed = true;
            }
            if (Color != part.Color) {
                Color = part.Color;
                changed = true;
            }
            if (HidesMask != part.HidesMask) {
                SetHides(part.HidesIds, part.HidesMask);
                changed = true;
            }
            return changed;
        }

        public bool Clear() {
            bool changed = false;
            if (SpriteId != null) {
                SpriteId = null;
                changed = true;
            }
            if (HidesMask != 0UL) {
                ClearHides();
                changed = true;
            }
            return changed;
        }

        public bool IsValid() {
            return Type != null;
        }

        public override string ToString() {
            return $"LookPart[{Index},{TypeId},{SpriteId},{Offset},{Scale},{Color},Hides:{HidesMask:X}]";
        }

        public override int GetHashCode() {
            var hc = Hashes.Quick(28137, Index);
            if (SpriteId != null) {
                hc = Hashes.Quick(hc, SpriteId.GetHashCode());
                hc = Hashes.Quick(hc, Color.GetHashCode());
                hc = Hashes.Quick(hc, HidesMask.GetHashCode());
            }
            return hc;
        }

    }
}
