using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class GenLooks {
        /// <summary>
        /// Sprite database for one LookPartTypeDef.SpriteCategory (e.g. "Bodies", "Eyes").
        /// Multiple part types can share a category (Eye_L / Eye_R both use "Eyes"), in which
        /// case they share the same sprite pool.
        /// </summary>
        private sealed class CategoryDb {
            public string Prefix;
            public bool EyeFiltering;
            public readonly Dictionary<string, List<string>> BySpecies = new();
        }
        private readonly Dictionary<string, CategoryDb> categories = new();
        private readonly List<string> tempList = new();

        public void BuildDB(ICollection<string> sprites) {
            // var start = Time.realtimeSinceStartupAsDouble;
            // LookPartTypeDef normally loads at SelfInit.Finalize, which runs after this. Load
            // it defensively here so modded part types' SpriteCategory is known in time.
            LookPartTypeDef.LoadKnown();
            categories.Clear();
            foreach (var type in LookPartTypeDef.All) {
                if (type.SpriteCategory == null) { continue; }
                if (!categories.TryGetValue(type.SpriteCategory, out var db)) {
                    db = new CategoryDb { Prefix = $"Beings/{type.SpriteCategory}/" };
                    categories[type.SpriteCategory] = db;
                }
                if (type.EyeSpriteFiltering) {
                    db.EyeFiltering = true;
                }
            }
            foreach (var fullSprite in sprites) {
                if (!fullSprite.StartsWith("Beings/")) { continue; }
                var sprite = fullSprite;
                // will match _R, _L, _U, _D
                if (sprite[^2] == '_') {
                    sprite = sprite.Remove(sprite.Length - 2);
                }
                // Will further remove _1 from animations
                if (sprite[^2] == '_') {
                    sprite = sprite.Remove(sprite.Length - 2);
                }
                CategoryDb matched = null;
                foreach (var db in categories.Values) {
                    if (sprite.StartsWith(db.Prefix)) {
                        matched = db;
                        break;
                    }
                }
                if (matched == null) { continue; }
                if (matched.EyeFiltering) {
                    // special sprites that don't have tags (like dead / dazed)
                    if (!sprite.Contains('[')) { continue; }
                    // skip closed eye versions
                    if (sprite.Contains("_Closed")) { continue; }
                    // Will further remove (L)/(R) from eyes
                    if (sprite[^1] == ')') {
                        sprite = sprite.Remove(sprite.Length - 3);
                    }
                }
                // Horizontal sprites are excluded
                if (sprite.EndsWith("_H")) { continue; }
                SetPartByCategory(sprite, matched.Prefix, matched.BySpecies);
            }
            // var end = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Looks build db took {0:0.00}s", end - start);
        }

        private void SetPartByCategory(string id, string prefix, Dictionary<string, List<string>> dict) {
            var idSplit = id.Replace(prefix, "");
            var sp = idSplit.Split(new char[] { '/' }, 2);
            if (sp.Length != 2) {
                D.Err("Invalid species part type with prefix {0}: {1}", prefix, id);
                return;
            }
            var species = sp[0];
            dict.GetInit(species).AddUniqueInefficient(id);
        }

        private bool LoadForType(List<string> input, List<string> output,
                char type, bool includeNeutral = false) {
            int found = 0;
            var typeTag = $"[{type}]";
            for (int i = 0; i < input.Count; i++) {
                string item = input[i];
                if (item.Contains(typeTag)) {
                    output.Add(item);
                    found++;
                }
            }
            if (includeNeutral && type != 'N') {
                for (int i = 0; i < input.Count; i++) {
                    string item = input[i];
                    if (item.Contains("[N]")) {
                        output.Add(item);
                        found++;
                    }
                }
            }
            return found > 0;
        }

        public void Generate(Rng rng, BeingData data, SpeciesLooksDef def, GenBeingVars vars) {
            if (data.Looks == null) {
                def.InitializeFor(data);
            }
            var looks = data.Looks;
            var species = data.Species;
            var body = data.Body;

            for (int i = looks.NumParts - 1; i >= 0; i--) {
                var part = looks.Parts[i];
                part.SpriteId = RandomSpriteFor(looks, rng, species, body, part.Type, vars);
                part.Color = ColorFor(rng, species, body, part.Type);
            }
        }

        public void GenerateMissing(Rng rng, BeingData data, LookPart part) {
            var looks = data.Looks;
            var species = data.Species;
            var body = data.Body;
            var vars = BuildVars(rng, data);

            part.SpriteId = RandomSpriteFor(looks, rng, species, body, part.Type, vars);
            part.Color = ColorFor(rng, species, body, part.Type);

            // D.Warn("Generated missing part for {0}: {1} -> {2} ({3})",
            //     data, part.Type, part.SpriteId, part.Color);
        }

        /// <summary>
        /// Regenerates every look part at once using a single shared GenBeingVars, so all parts
        /// (body/head/hair/eyes/...) stay consistent with each other. Used as a fallback when a
        /// being's saved looks can't be matched against current part defs (e.g. a pre-LookPartTypeDef
        /// save).
        /// </summary>
        public GenBeingVars RegenerateAll(Rng rng, BeingData data) {
            var vars = BuildVars(rng, data);
            Generate(rng, data, data.Species.LooksDef, vars);
            data.Looks.BodyType = vars.BodyType;
            return vars;
        }

        /// <summary>
        /// Rolls a BodyType/HeadType/HairType combo for a being whose body already exists
        /// (Sex is known or rolled fresh). Shared by GenerateMissing and RegenerateAll so every
        /// part picked against the same vars stays visually coherent.
        /// </summary>
        private GenBeingVars BuildVars(Rng rng, BeingData data) {
            var vars = new GenBeingVars(data.Species, Systems.Naming.BeingType.None);
            var sex = data.Body.Sex;
            if (sex == null) {
                sex = data.Species.RandomSex(rng);
                vars.Sex = sex;
            }
            if (sex != null) {
                vars.BodyType = rng.CharFromStr(sex.BodyTypes, 'N', sex.BodyTypeChances);
                vars.HeadType = rng.CharFromStr(sex.HeadTypes, 'N', null);
                vars.HairType = rng.CharFromStr(sex.HairTypes, 'N', null);
            } else {
                vars.BodyType = 'N';
                vars.HeadType = 'N';
                vars.HairType = 'N';
            }
            return vars;
        }

        private Color ColorFor(Rng rng, Species species, Body body, LookPartTypeDef type) {
            switch (type.ColorSource) {
                case "Skin": return Palette.ColorById(body.SkinColorId);
                case "Hair": return Palette.ColorById(body.HairColorId);
                case "EyeL": return Palette.ColorById(body.EyeColorLId);
                case "EyeR": return Palette.ColorById(body.EyeColorRId);
                default: return RandomColorFor(rng, species, type);
            }
        }

        private Color RandomColorFor(Rng rng, Species species, LookPartTypeDef type) {
            var colorGroupKey = $"{species.Id}.{type.Id}"; // Human.Body
            var col = Palette.RandomForGroup(colorGroupKey, rng);
            if (col == null) {
                return Palette.White;
            }
            return col.Color;
        }

        private string RandomSpriteFor(BeingLooks looks,
                Rng rng, Species species, Body body,
                LookPartTypeDef type, GenBeingVars vars) {
            var sex = body.Sex;
            switch (type.Gen) {
                case LookPartGenStrategy.DerivedFromBody: {
                    var bodyType = LookPartTypeDef.Body;
                    string bodySprite = null;
                    var bodyLookPart = looks.Get(bodyType);
                    if (bodyLookPart != null) {
                        bodySprite = bodyLookPart.SpriteId;
                    }
                    if (bodySprite == null) {
                        bodySprite = SpriteFor(bodyType, species, vars.TagFor(bodyType.VarTag), rng);
                    }
                    if (bodySprite == null) { return null; }
                    var parts = bodySprite.Split('/');
                    var speciesPart = parts[^2];

                    string derivedSprite = bodySprite;
                    if (speciesPart != species.Id) {
                        // handle mobilizer overrides that can turn /Robot/ into /Robot_Tracks/, etc
                        derivedSprite = bodySprite.Replace(
                            $"/{speciesPart}/", $"/{species.Id}/");
                    }
                    return derivedSprite
                        .Replace($"/{bodyType.SpriteCategory}/", $"/{type.SpriteCategory}/")
                        .Replace($"]{bodyType.Id}", $"]{type.Id}");
                }
                case LookPartGenStrategy.MirrorPair: {
                    if (type.MirrorOfType != null) {
                        var mirror = looks.Get(type.MirrorOfType);
                        if (mirror?.SpriteId != null) {
                            return mirror.SpriteId;
                        }
                    }
                    return SpriteFor(type, species, vars.TagFor(type.VarTag), rng, type.IncludeNeutral);
                }
                case LookPartGenStrategy.None:
                    return SpriteForInstalledBodyPart(body, type, vars);
                default: {
                    // Hair is the only vanilla part with a "may grow nothing" chance
                    if (type == LookPartTypeDef.Hair && sex != null && rng.Chance(sex.ChanceBald)) {
                        return null;
                    }
                    return SpriteFor(type, species, vars.TagFor(type.VarTag), rng, type.IncludeNeutral);
                }
            }
        }

        /// <summary>
        /// For Gen:None look parts, the sprite (if any) comes from an installed BodyPartDef that
        /// declares this type as its LooksPart (BodyPartDef.LooksPart/LooksSprite - see
        /// Docs/Architecture/Systems/BeingLooks.md). Equipment-driven None parts (Hat/Clothing/
        /// Weapon) have no matching BodyPartDef and simply resolve to null here, same as before
        /// this lookup existed. Covers generation/regen time (this method), which is the one
        /// place body-part-driven looks aren't already kept in sync by
        /// BodyPartSlot.ApplyDrivenLook (that only fires on runtime install/remove/load).
        /// </summary>
        private string SpriteForInstalledBodyPart(Body body, LookPartTypeDef type, GenBeingVars vars) {
            if (body?.Slots == null) { return null; }
            foreach (var slot in body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                var def = slot.InstalledDef;
                if (def != null && def.LooksPartType == type) {
                    return BeingLooks.SpriteForBodyType(vars.BodyType, def.LooksSprite);
                }
            }
            return null;
        }

        private string SpriteFor(LookPartTypeDef partType,
                Species species, char varType, Rng rng, bool includeNeutral = false) {
            var looksId = species.Looks;
            if (varType == '\0') {
                D.Err("Passing invalid type while generating sprites: {0}", species);
                return null;
            }
            if (!categories.TryGetValue(partType.SpriteCategory, out var db)) {
                D.Err("No sprite category registered for look part type: {0}", partType);
                return null;
            }
            if (db.BySpecies.TryGetValue(looksId, out var options)) {
                tempList.Clear();
                if (LoadForType(options, tempList, varType, includeNeutral)) {
                    return rng.From(tempList);
                }
                D.Err("No sprite found for looks {0} with type {1}. ({2}). Options: {3}",
                    looksId, varType, species, string.Join(", ", options));
                return null;
            }
            D.Err("No looks options found for {0} ({1}). Options: {2}",
                looksId, species, db.BySpecies.Keys);
            return null;
        }
    }
}
