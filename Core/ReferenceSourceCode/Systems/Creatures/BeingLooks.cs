using KL.Utils;
using Game.Utils;
using Game.Constants;
using Game.Rendering;
using MessagePack;

namespace Game.Systems.Creatures {
    [MessagePackObject]
    public class BeingLooks {
        [Key("Parts")] public LookPart[] Parts;
        [IgnoreMember] public int NumParts;
        [IgnoreMember] public bool IsDirty;
        [SkipDumpInstance]
        [IgnoreMember] public BeingAvatar Avatar;
        [SkipDumpInstance]
        [IgnoreMember] public BeingSurgeryPreview SurgeryPreview;
        /// <summary>
        /// Required for inventory looks generation
        /// </summary>
        [Key("BodyType")] public char BodyType;
        [IgnoreMember] public int HeadPartIdx;

        public LookPart Get(LookPartTypeDef type)  {
            if (Parts == null) { return null; }
            for (int i = Parts.Length - 1; i >= 0; i--) {
                LookPart p = Parts[i];
                if (p.Type == type) {
                    return p;
                }
            }
            return null;
        }

        /// <summary>
        /// Replaces the "[BODY_TYPE]" token in a sprite id with the being's body-type tag, then
        /// falls back to Neutral ("N") if no sprite was ever shipped for that specific tag (e.g.
        /// a mod that only ships [N] art with no separate [M]/[F] variants). Shared convention
        /// between equipment-driven looks (InventoryItemSlot) and body-part-driven looks
        /// (BodyPartSlot.ApplyDrivenLook, GenLooks) - see Docs/Architecture/Systems/BeingLooks.md.
        /// </summary>
        public static string SpriteForBodyType(char bodyType, string spriteId) {
            if (spriteId == null || !spriteId.Contains("[BODY_TYPE]")) {
                return spriteId;
            }
            var resolved = spriteId.Replace("BODY_TYPE", bodyType.ToString());
            if (bodyType != 'N' && !HasSprite(resolved)) {
                resolved = spriteId.Replace("BODY_TYPE", "N");
            }
            return resolved;
        }

        /// <summary>
        /// Down-facing is treated as the canonical "does this art exist at all" probe, matching
        /// the convention that a part ships all 4 facings or none (BeingLooksComp.GetFacingSprite's
        /// own flip-fallback relies on the same assumption).
        /// </summary>
        private static bool HasSprite(string baseSpriteId) {
            return RenderingService.Sprites.TryFind(
                Facing.FaceSpriteTo(baseSpriteId, Facing.Type.Down), out _);
        }

        public bool HasPart(LookPartTypeDef partType) {
            for (int i = Parts.Length - 1; i >= 0; i--) {
                LookPart p = Parts[i];
                if (p.Type == partType) {
                    return true;
                }
            }
            return false;
        }

        public bool LoadPart(LookPart part, object context) {
            if (!part.IsValid()) {
                D.Err("Trying to set an invalid look part on being: {0} (on {1})",
                    part, context);
                return false;
            }
            var lpc = Parts.Length;
            for (int i = lpc - 1; i >= 0; i--) {
                var item = Parts[i];
                if (item.Type == part.Type) {
                    if (item.CopyLookFrom(part)) {
                        IsDirty = true;
                    }
                    break;
                }
            }
            return IsDirty;
        }

        /// <summary>
        /// Resolves Type/HidesMask caches on every part from their TypeId/HidesIds strings.
        /// Must be called once after this BeingLooks comes straight out of a save file (MessagePack
        /// deserialize only populates the [Key] string fields).
        /// </summary>
        public void ResolvePostLoad() {
            if (Parts == null) { return; }
            for (int i = Parts.Length - 1; i >= 0; i--) {
                Parts[i].ResolvePostLoad();
            }
        }

        /// <summary>
        /// True when every part failed to resolve a TypeId (i.e. this came from a pre-LookPartTypeDef
        /// save). Such saves can't be matched against current part defs and should be regenerated.
        /// </summary>
        public bool IsLegacy() {
            if (Parts == null || Parts.Length == 0) { return false; }
            for (int i = Parts.Length - 1; i >= 0; i--) {
                if (Parts[i].TypeId != null) { return false; }
            }
            return true;
        }

        public override int GetHashCode() {
            var hc = 13;
            for (int i = 0; i < Parts.Length; i++) {
                LookPart p = Parts[i];
                hc = Hashes.Quick(hc, p.GetHashCode());
            }
            return hc;
        }

        public override string ToString() {
            if (Parts != null) {
                return $"Looks[{Units.Num(Parts.Length)}]";
            }
            return "Looks[Empty]";
        }
    }
}
