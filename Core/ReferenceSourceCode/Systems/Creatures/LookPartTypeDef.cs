using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Creatures {
    /// <summary>
    /// How GenLooks should pick a sprite for a part of this type.
    /// </summary>
    public enum LookPartGenStrategy {
        /// <summary>Pick a random sprite from this type's SpriteCategory db, filtered by VarTag.</summary>
        Standard,
        /// <summary>Derive the sprite from the Body part's sprite (used by Shadow).</summary>
        DerivedFromBody,
        /// <summary>Reuse the MirrorOf part's sprite if it already has one, else fall back to Standard.</summary>
        MirrorPair,
        /// <summary>Never generates a sprite (e.g. Hat, Clothing, Weapon are equipment-driven).</summary>
        None
    }

    /// <summary>
    /// Moddable replacement for the old LookPartType enum. Each instance is a body "look" part
    /// type (Body, Head, Hair, Eye_L, ... or a modded one like Tail) loaded from
    /// Config/Species/LookPartTypes/*.json. Identity is an interned string Id / int IdH.
    /// Set membership (Hides, IsFace) uses a stable bit assigned at load (BitIndex -> 1UL &lt;&lt; BitIndex),
    /// so runtime checks stay int/ulong bitwise tests, never string comparisons.
    /// </summary>
    public sealed class LookPartTypeDef {
        private static readonly Dictionary<int, LookPartTypeDef> all = new();
        // tracks which bit indices are taken so omitted BitIndex can auto-assign the lowest free one
        private static readonly bool[] usedBits = new bool[64];

        public static readonly LookPartTypeDef None = new() {
            Id = "None",
            IdH = Hashes.S("None"),
            BitIndex = -1,
            Bit = 0UL,
        };

        [ExecuteOnReload]
        private static void ClearStatics() {
            all.Clear();
            for (int i = 0; i < usedBits.Length; i++) { usedBits[i] = false; }
            ResetCachedRefs();
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Register() {
            LoadKnown();
        }

        public static void LoadKnown() {
            if (all.Count != 0) { return; }
            var types = The.ModLoader.LoadObjects<LookPartTypeDef>("Config/Species/LookPartTypes");
            // first pass: honor explicit BitIndex values
            foreach (var t in types) {
                t.IdH = Hashes.S(t.Id);
                if (t.BitIndex < 0) { continue; }
                if (t.BitIndex > 63) {
                    D.Err("LookPartTypeDef {0} has BitIndex {1} out of range (0-63)!", t.Id, t.BitIndex);
                    t.BitIndex = -1;
                    continue;
                }
                if (usedBits[t.BitIndex]) {
                    D.Err("LookPartTypeDef {0} has BitIndex {1} that collides with another type!",
                        t.Id, t.BitIndex);
                    t.BitIndex = -1;
                    continue;
                }
                usedBits[t.BitIndex] = true;
            }
            // second pass: auto-assign the lowest free bit to anything left unresolved
            foreach (var t in types) {
                if (t.BitIndex >= 0) { continue; }
                int free = NextFreeBit();
                if (free < 0) {
                    D.Err("Cannot assign a bit to LookPartTypeDef {0}: all 64 bits are taken!", t.Id);
                    continue;
                }
                usedBits[free] = true;
                t.BitIndex = free;
            }
            foreach (var t in types) {
                t.Bit = t.BitIndex >= 0 ? 1UL << t.BitIndex : 0UL;
                if (!all.TryAdd(t.IdH, t)) {
                    D.Err("Cannot add LookPartTypeDef: {0}. Already added!", t.Id);
                }
            }
            // second resolve pass: string cross-references (MirrorOf)
            foreach (var t in types) {
                if (t.MirrorOf == null) { continue; }
                t.MirrorOfType = Get(t.MirrorOf);
            }
            // resolve directional sort order names to SpriteOrder ints, once
            foreach (var t in types) {
                if (t.SortOrderDirectional == null) { continue; }
                if (t.SortOrderDirectional.Length != 4) {
                    D.Err("LookPartTypeDef {0} has SortOrderDirectional with {1} entries, expected 4 (Down/Left/Up/Right)!",
                        t.Id, t.SortOrderDirectional.Length);
                    continue;
                }
                t.SortOrderDirectionalResolved = new int[4];
                for (int i = 0; i < 4; i++) {
                    t.SortOrderDirectionalResolved[i] = SpriteOrder.ForName(t.SortOrderDirectional[i], t.Id);
                }
            }
        }

        private static int NextFreeBit() {
            for (int i = 0; i < usedBits.Length; i++) {
                if (!usedBits[i]) { return i; }
            }
            return -1;
        }

        public static LookPartTypeDef Get(string id) {
            return Get(id, Hashes.S(id));
        }

        public static LookPartTypeDef Get(string id, int idH) {
            if (id == "None") { return None; }
            if (all.TryGetValue(idH, out var def)) {
                return def;
            }
            D.Err("LookPartTypeDef with id {0} (hash {1}) is not defined! See Config/Species/LookPartTypes folder",
                id, idH);
            return null;
        }

        public static LookPartTypeDef Get(int idH) {
            if (idH == None.IdH) { return None; }
            if (all.TryGetValue(idH, out var def)) {
                return def;
            }
            return null;
        }

        public static IEnumerable<LookPartTypeDef> All => all.Values;

        // Cached accessors for the handful of vanilla types C# code names directly.
        // Resolved lazily and once; reference compares from here on are build-time only.
        private static LookPartTypeDef body, head, hair, eyeL, eyeR, face, shadow, hat, weapon, clothing;
        public static LookPartTypeDef Body => body ??= Get("Body");
        public static LookPartTypeDef Head => head ??= Get("Head");
        public static LookPartTypeDef Hair => hair ??= Get("Hair");
        public static LookPartTypeDef Eye_L => eyeL ??= Get("Eye_L");
        public static LookPartTypeDef Eye_R => eyeR ??= Get("Eye_R");
        public static LookPartTypeDef Face => face ??= Get("Face");
        public static LookPartTypeDef Shadow => shadow ??= Get("Shadow");
        public static LookPartTypeDef Hat => hat ??= Get("Hat");
        public static LookPartTypeDef Weapon => weapon ??= Get("Weapon");
        public static LookPartTypeDef Clothing => clothing ??= Get("Clothing");

        private static void ResetCachedRefs() {
            body = head = hair = eyeL = eyeR = face = shadow = hat = weapon = clothing = null;
        }

        public string Id;
        public int IdH;
        /// <summary>
        /// Stable bit index (0-63) for this type, used to build the Bit mask. Mods may omit this
        /// and the lowest free index will be auto-assigned at load.
        /// </summary>
        public int BitIndex = -1;
        [System.NonSerialized] public ulong Bit;
        /// <summary>
        /// Part counts toward LookPart.IsFace / the FaceFull grouping (eyes, face overlay).
        /// </summary>
        public bool IsFace;
        /// <summary>
        /// SpriteOrder constant name (see Constants.SpriteOrder.ForName), e.g. "BeingBody".
        /// </summary>
        public string SortOrder;
        /// <summary>
        /// Optional per-facing override of SortOrder: 4 SpriteOrder constant names in
        /// Facing.Type order (Down, Left, Up, Right). Lets a part (e.g. a Tail) draw behind
        /// the body in one facing and in front of it in another. Null (the default) means
        /// every facing uses the single SortOrder above.
        /// </summary>
        public string[] SortOrderDirectional;
        [System.NonSerialized] public int[] SortOrderDirectionalResolved;
        /// <summary>
        /// If true, the species' SortOrderAdjustment is NOT added on top (Shadow's policy).
        /// </summary>
        public bool IgnoreSortAdjustment;
        /// <summary>
        /// Sprite DB folder under Beings/, e.g. "Bodies", "Heads", "Tails".
        /// </summary>
        public string SpriteCategory;
        public LookPartGenStrategy Gen = LookPartGenStrategy.Standard;
        /// <summary>
        /// Which GenBeingVars tag char drives sprite selection: "Body" / "Head" / "Hair".
        /// </summary>
        public string VarTag = "Body";
        /// <summary>
        /// Tint source: "Skin" / "Hair" / "EyeL" / "EyeR" (read straight off BeingData.Body), or
        /// "PaletteGroup" (default; looks up a random color from the "{species}.{Id}" palette
        /// group via Palette.RandomForGroup, falling back to white if undefined).
        /// </summary>
        public string ColorSource = "PaletteGroup";
        /// <summary>
        /// For MirrorPair gen strategy: the other part type id this one mirrors (e.g. Eye_L &lt;-&gt; Eye_R).
        /// </summary>
        public string MirrorOf;
        [System.NonSerialized] public LookPartTypeDef MirrorOfType;
        /// <summary>
        /// Include "[N]" (neutral) sprites in addition to sex/type-tagged ones when picking randomly.
        /// </summary>
        public bool IncludeNeutral;
        /// <summary>
        /// Applies the eye-specific sprite DB preprocessing (skip untagged "state" sprites, skip
        /// "_Closed" variants, strip "(L)"/"(R)" suffixes). True for Eye_L / Eye_R.
        /// </summary>
        public bool EyeSpriteFiltering;
        /// <summary>
        /// Hidden (alpha 0) while the being lies in a bed slot that HidesBody — only parts NOT
        /// flagged stay visible (typically just the Head). De-hardcodes BedSlot's old Body/
        /// Clothing/Hat list; a modded part (e.g. Tail) opts in by setting this true.
        /// </summary>
        public bool HideInBed;

        public override string ToString() {
            return $"LookPartTypeDef[{Id}]";
        }
    }
}
