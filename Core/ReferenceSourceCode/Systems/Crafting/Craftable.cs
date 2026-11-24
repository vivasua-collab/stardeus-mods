using System;
using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.UI;
using Game.Systems.UIUX;
using Game.Constants;
using System.Diagnostics;
using Game.Mods;
using System.IO;
using Game.Migrations;
using Game.Systems.BodyParts;
using KL.Randomness;
using System.Linq;

namespace Game.Systems.Crafting {
    [Serializable]
    public class Craftable {
        [SelfInit]
        public static void Reset() {
            isLoaded = false;
            all.Clear();
            byDef.Clear();
            productsByIngredient.Clear();
            CraftersByType.Clear();
            allOrdered = null;
        }
        private static readonly Dictionary<int, Craftable> all = new();
        private static Craftable[] allOrdered;
        /// <summary>
        /// The order is reversed so a reverse for loop should be used to list it correctly
        /// </summary>
        public static Craftable[] AllOrdered {
            get {
                if (allOrdered != null) { return allOrdered; }
                allOrdered = all.Values.OrderByDescending(c => c.ProductDef.NameT).ToArray();
                return allOrdered;
            }
        }
        public static IEnumerable<Craftable> All => all.Values;
        private static readonly Dictionary<Def, List<Craftable>> byDef = new();
        // Reverse index of byDef: MatType -> defs crafted using it. Used by the
        // codex "Ingredient" block, which renders ExtendedDesc per build-menu item,
        // so per-def full scans of The.Defs.Defs are avoided for this lookup.
        private static readonly Dictionary<MatType, List<Def>> productsByIngredient = new();
        public static readonly Dictionary<string, List<Def>> CraftersByType = new();
        public string Id;
        public int IdH;
        // Only a crafter with compatible type will be able to craft this
        // blueprint
        public string Type;
        // When color group ID is not null, we will add color controls to the craftable
        // and won't let select crafting of each individual color separately
        public string ColorGroupId;
        // Only for materials. How much of that mat it will produce
        public int OutputMultiplier;
        // Material types with amounts
        public Mat[] Ingredients;
        /// <summary>
        /// If this is defined in the craftable configuration, this output will be used
        /// instead of ingredients. Use case: cloning a human doesn't require Leather, but
        /// disposing of a human should produce Leather. Thus HumanBody output has to have Leather
        /// </summary>
        public Mat[] DisposableOutput;
        public string ProductDefId;
        private Def productDef;
        public Def ProductDef => productDef ??= The.Defs.Get(ProductDefId);
        // Time in hours - how long it takes to print this shit
        public float ProductionTimeHours;
        public float EnergyCost;
        // If items get converted from X to Y via alchemy or stuff, we don't want
        // these items to participate in stock market validation
        public bool IgnoreMarket;
        /// <summary>
        /// Will ignore energy output validation inconsistencies.
        /// This is for things like Biofuel, which is more potent than Biowaste
        /// </summary>
        public bool IgnoreEnergyOutput;

        private static bool isLoaded;
        /// <summary>
        /// Bump it up to move crafing recipes lower in the codex entry
        /// </summary>
        public int ListOrder;

        public static void ForCrafter(HashSet<string> compatibleTypes, List<Craftable> output) {
            output.Clear();
            foreach (var c in all) {
                if (compatibleTypes.Contains(c.Value.Type)) {
                    output.Add(c.Value);
                }
            }
        }

        public static void LoadKnown() {
            if (isLoaded) { return; }
            isLoaded = true;
            var types = The.ModLoader.LoadObjects<Craftable>("Config/Craftable");
            foreach (var c in types) {
                if (c.Type == null) {
                    D.Err("Craftable type is null for craftable: {0}", c.Id);
                    continue;
                }
                c.IdH = Hashes.S(c.Id);
                if (all.ContainsKey(c.IdH)) {
                    var msg = $"Trying to load Craftable with Id that already exists: {c.Id}";
                    D.Err(msg);
                    WPopup.Create(true, IconId.CWarning, "GAME BREAKING BUG", msg).Spawn();
                } else {
                    c.Deserialize();
                    if (c.OutputMultiplier < 1) {
                        c.OutputMultiplier = 1;
                    }
                    var prodDef = c.ProductDef;
                    if (prodDef == null) {
                        D.Err("Craftable product def is unavailable: {0}->{1}", c.Id, c.ProductDefId);
                        continue;
                    }
                    if (!byDef.TryGetValue(c.ProductDef, out var list)) {
                        list = new List<Craftable>();
                        byDef.Add(c.ProductDef, list);
                    }
                    all.Add(c.IdH, c);
                    list.Add(c);
                    if (!Arrays.IsEmpty(c.Ingredients)) {
                        foreach (var ing in c.Ingredients) {
                            // Skip broken mod recipes and self-referencing recipes
                            // (e.g. CrawlerGoo consumes Crawler Goo to produce more)
                            if (ing.Type == null || ing.Type.Def == prodDef) { continue; }
                            if (!productsByIngredient.TryGetValue(ing.Type, out var products)) {
                                products = new List<Def>();
                                productsByIngredient.Add(ing.Type, products);
                            }
                            if (!products.Contains(prodDef)) {
                                products.Add(prodDef);
                            }
                        }
                    }
                }
            }
            foreach (var c in byDef) {
                c.Key.Recipes = c.Value;
            }
        }

        public static Craftable Find(int idH) {
            if (all.TryGetValue(idH, out var cr)) {
                return cr;
            }
            return cr;
        }

        public static bool IsCraftable(Def def) {
            return byDef.ContainsKey(def);
        }

        public static List<Craftable> FindForDef(Def def) {
            if (def == null) { return null; }
            if (byDef.Count == 0) {
                D.Err("Trying to find craftable too early!");
            }
            if (byDef.TryGetValue(def, out var res)) {
                return res;
            }
            return null;
        }

        public static List<Def> FindProductsUsing(MatType mat) {
            return productsByIngredient.Get(mat, null);
        }
        public static List<Craftable> FindDisposablesFor(Def def) {
            if (def == null) { return null; }
            var mt = def.MatType;
            if (mt == null) { return null; }
            List<Craftable> res = null;
            foreach (var cr in all.Values) {
                if (cr.DisposableOutput != null) {
                    foreach (var mat in cr.DisposableOutput) {
                        if (mat.Type == mt) {
                            if (res == null) {
                                res = new List<Craftable>();
                            }
                            res.Add(cr);
                            break;
                        }
                    }
                }
            }
            return res;
        }

        public override string ToString() {
            return $"Craftable[{Id}({Type})]";
        }
        public List<Def> Crafters {
            get {
                if (Type == null) {
                    D.Err("Craftable with type null is trying to get list of crafters! {0}", this);
                    return null;
                }
                return CraftersByType.Get(Type, null);
            }
        }

        // It's ok if ingredients are empty, craftable could be blank
        public void Deserialize() {
            bool hadIO = false;
            if (!Arrays.IsEmpty(Ingredients)) {
                hadIO = true;
                for (int i = 0; i < Ingredients.Length; i++) {
                    if (Ingredients[i].TryDeserialize(out var mat)) {
                        Ingredients[i] = mat;
                    }
                }
            } else {
                Ingredients = null;
            }
            if (!Arrays.IsEmpty(DisposableOutput))  {
                hadIO = true;
                for (int i = 0; i < DisposableOutput.Length; i++) {
                    if (DisposableOutput[i].TryDeserialize(out var mat)) {
                        DisposableOutput[i] = mat;
                    }
                }
            } else {
                DisposableOutput = null;
            }
            if (!hadIO) {
                D.Err("Craftable has no Ingredients and no DisposableOutput! {0}", Id);
            }
        }

        public static bool CanHaveColor(Def def) {
            if (byDef.TryGetValue(def, out var res)) {
                return res[0].ColorGroupId != null;
            }
            return false;
        }

        public static Color RandomColorFor(Def def, Rng rng) {
            if (byDef.TryGetValue(def, out var res)) {
                var gId = res[0].ColorGroupId;
                if (gId != null) {
                    return Palette.RandomForGroup(gId, rng).Color;
                }
            }
            return Palette.White;
        }
    }
}
