#if UNITY_EDITOR
// #define DEBUG_BODY_PARTS
#endif
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.BodyParts {
    public sealed class BodyPartType {
        public string Id;
        public int IdH;
        public string NameT;
        public BodyPartFlags Flags;
        public bool IsRemovable => (Flags & BodyPartFlags.Removable) == BodyPartFlags.Removable;
        public bool IsEssential => (Flags & BodyPartFlags.Essential) == BodyPartFlags.Essential;
        public bool IsOptional => (Flags & BodyPartFlags.Optional) == BodyPartFlags.Optional;
        public float Size;
        // when we hit this with something that can cause internal damage
        // this will be our chance to damage internal organ
        public float SubpartHitChance;
        public float ExtractRisk;
        public float ImplantRisk;
        public float FatalityRisk;
        public float RejectionRisk;
        public float SurgicalFatigue;
        public string IconId;
        public string AttachedTo;
        public BodyPartType AttachedToPart;
        private static readonly Dictionary<int, BodyPartType> all = new();
        [JsonEnum(typeof(Game.Systems.BodyParts.BodyPartFlags))]
        public string[] PartFlags;

        [SelfInit]
        private static void Init() {
            all.Clear();
        }

        public static void LoadKnown() {
            if (all.Count != 0) { return; }
            var types = The.ModLoader.LoadObjects<BodyPartType>("Config/Body/PartTypes");
            foreach (var s in types) {
                s.IdH = Hashes.S(s.Id);
                if (!all.TryAdd(s.IdH, s)) {
                    D.Err("Cannot add body part type: {0}. Already added!", s.Id);
                }
                if (s.IconId == null) {
                    #if DEBUG_BODY_PARTS
                    D.Err("IconId missing for body part type: {0}", s.Id);
                    #endif
                    s.IconId = Constants.IconId.CCog;
                }
                if (!Arrays.IsEmpty(s.PartFlags)) {
                    foreach (var f in s.PartFlags) {
                        var fen = Enums.FromString<BodyPartFlags>(f);
                        s.Flags |= fen;
                    }
                }

                if (s.Size < 0.0001f) {
                    D.Err("Body part has non-positive Size! {0}", s.Id);
                    s.Size = 0.25f;
                }

                s.NameT = $"bodypart.type.{s.Id}".T();
            }
            // resolve attachments
            foreach (var type in all.Values) {
                if (type.AttachedTo == null && type.Id != "Body") {
                    type.AttachedTo = "Body";
                }
                if (type.AttachedTo != null) {
                    if (all.TryGetValue(Hashes.S(type.AttachedTo), out var part)) {
                        type.AttachedToPart = part;
                    } else {
                        D.Err("Incorrect AttachedTo ({0}) in definition of body part type: {1}",
                            type.AttachedTo, type.Id);
                    }
                }
            }
        }

        public static BodyPartType Get(string type) {
            return Get(type, Hashes.S(type));
        }

        public static BodyPartType Get(string type, int typeH) {
            if (all.TryGetValue(typeH, out var part)) {
                return part;
            }
            D.Err("BodyPartType with id {0} (hash {1}) is not defined! See Config/Body/PartTypes folder",
                    type, typeH);
            return null;
        }
        public static BodyPartType Get(int type) {
            if (all.TryGetValue(type, out var part)) {
                return part;
            }
            D.Err("BodyPartType with idHash {0} is not defined! See Config/Body/PartTypes folder",
                    type);
            return null;
        }

        public override string ToString() {
            return $"BodyPartType[{Id}]";
        }
    }
}
