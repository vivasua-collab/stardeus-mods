using System;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Crafting {
    // What a crafting device can make, read from its def for the codex. The build menu
    // renders ExtendedDesc for every item, so the recipe list is cached per def
    public static class CrafterDefInfo {
        private static readonly Dictionary<Def, List<Craftable>> recipesByCrafter = new();
        private static readonly List<Def> productsBuf = new();

        [SelfInit]
        public static void Reset() {
            recipesByCrafter.Clear();
        }

        // Recipes with ingredients only, disposal recipes are not made by crafters
        public static List<Craftable> RecipesFor(Def crafter) {
            if (recipesByCrafter.TryGetValue(crafter, out var list)) {
                return list;
            }
            var config = crafter.ComponentConfigFor(ComponentIdH.MultiCrafter, false);
            var types = config?.GetStringSet(PropertyIdH.CompatibleTypes, null);
            if (types == null) {
                return null;
            }
            list = new List<Craftable>();
            var anyLoaded = false;
            foreach (var cr in Craftable.All) {
                anyLoaded = true;
                if (Arrays.IsEmpty(cr.Ingredients)) { continue; }
                if (Array.IndexOf(types, cr.Type) < 0) { continue; }
                list.Add(cr);
            }
            // Asked before craftables were loaded: do not cache the empty answer
            if (anyLoaded) {
                recipesByCrafter[crafter] = list;
            }
            return list;
        }

        // Batch size 1, the way CraftingSys bills a single item
        public static void AddPower(Def crafter, ref DefPower power) {
            var recipes = RecipesFor(crafter);
            if (recipes == null || recipes.Count == 0) { return; }
            var min = float.MaxValue;
            var max = 0f;
            foreach (var cr in recipes) {
                var cost = cr.EnergyCost * Tunables.CraftingEnergyCost;
                if (cost < min) {
                    min = cost;
                }
                if (cost > max) {
                    max = cost;
                }
            }
            power.AddConsumption(min, max);
        }

        public static void AppendProducts(StringBuilder sb, Def crafter, int cap) {
            var recipes = RecipesFor(crafter);
            if (recipes == null || recipes.Count == 0) { return; }
            productsBuf.Clear();
            foreach (var cr in recipes) {
                var product = cr.ProductDef;
                if (product == null || productsBuf.Contains(product)) { continue; }
                if (!product.IsInSeason && !A.InstantBuild) { continue; }
                productsBuf.Add(product);
            }
            if (productsBuf.Count == 0) { return; }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(T.VerbProduces));
            DefUnlockOrder.AppendLinks(sb, productsBuf, cap);
            productsBuf.Clear();
        }
    }
}
