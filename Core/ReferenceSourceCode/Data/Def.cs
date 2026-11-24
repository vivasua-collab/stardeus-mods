#if !UNITY_EDITOR
// #define CACHE_EXTENDED_DESC
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data.Economy;
using Game.Input;
using Game.Mods;
using Game.Procgen;
using Game.Research;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.AI;
using Game.Systems.Stats;
using Game.Systems.Crafting;
using Game.Systems.UIUX;
using System.Collections;
using Game.Systems.Space;

namespace Game.Data {
    /// <summary>
    /// Def = Definition. Handles entity definitions that are defined in JSON files
    /// </summary>
    public sealed class Def {
        private const int maxNumIngr = 5;
        /*

        !!!! READ THIS WHEN ADDING NEW THINGS !!!!

        if you add new properties here, you will need to make sure that
        definition copy and merge works!

        also, rather than adding something new here, strongly consider
        adding it to some component properties instead, this already has more
        than enough garbage
        */
        private string preview;
        private Sprite previewSprite;
        // WARNING if new fields are added to this, make sure those fields
        // are properly copied when loading mods with parent definitions in
        // ModLoader!!!
        public bool IsAbstract;

        public bool IsUnfinished;
        // Hidden from all tools
        public bool SkipToolListing;
        public bool UseParentTexts;
        // Rules:
        // 1. higher tier cannot be in front of lower tier
        // 2. cheapest higher tier cannot cost less than most expensive lower tier
        // 3. tier cost grows in non linear way (tier 3 will not cost 3x of tier 1)
        public int Tier;
        // This will be used in formula that determines the cost of research that
        // unlocks this def
        public int ResearchValue;
        // Multiplier on this def's wealth value as a finished structure, on top of the default
        // formula (materials x tier multiplier + ship class points; see
        // NetWorthCalculator.WorthOfStructure). Exists because materials are a floor on what a
        // building is worth, not a measure of it - a shield emitter or a ship computer is worth
        // more than its parts. A multiplier rather than an absolute value so that retuning
        // materials or tiers still flows through. 1 = formula as-is. Note the parent-def merge
        // in ModLoader treats exactly 1 as "unset", so a child cannot reset a parent's override
        // back to 1 (use 1.0001 if that is ever needed).
        public float WealthValueMul = 1f;
        // Calculated at runtime, does not matter during initialization / mod loading
        public float InternalValue;
        public string Id;
        public int IdH;
        public bool IsImportant;
        public bool IgnoreMarket;
        // Real-world date window this def is available in, e.g. Halloween decorations.
        // Null means "not seasonal, always available". Checked by
        // ToolPlaceTiles.CanShowInBuildMenu (build menu / dev tools), GenSpawn (derelict
        // loot), and WResearchNode (research tree Unlock button + spoiler-free unlock
        // icons) for the owning research node's own Def.
        public SeasonalLock Seasonal;
        public bool IsInSeason => Seasonal == null || CodexExportScope.IsActive || Seasonal.IsInSeason;
        [NonSerialized]
        public bool IsTemplate;
        [NonSerialized] public bool IsConstructable;
        [NonSerialized] public bool IsPlacementInSpace;
        // For research nodes only
        [NonSerialized] public List<Def> Unlockables;
        [NonSerialized] public string ModId;
        [NonSerialized] public bool IsCoreMod;
        public string ToolPath;
        // Variants of one buildable (e.g. Floor01-04) that share a build menu button. Never
        // inherited from ParentId: parents like Floor01 also have unrelated children (TrapFloor,
        // FloorVent...), so each member names its group itself.
        public string BuildGroup;

        // List of other definition Ids that have to be unlocked
        // In order for this one to be unlocked.
        // If this is null - it's unlocked right from the start
        public Researchable Researchable;
        // Defines chances and checks for object to be spawned in the map
        public GenSpawnable Spawnable;
        public List<Craftable> Recipes;
        public string NameKey;
        public string DescKey;
        private string _searchNameT;
        public string SearchNameT => _searchNameT ??= Strings.ToSearchString(NameT);
        private string _codexLink;
        public string CodexLink => _codexLink ??= Texts.LinkToCodex(this);
        // Def icons are built after the defs, so a link made before them is made again
        internal void ResetCodexLink() {
            _codexLink = null;
        }
        private string _nameT;

        public string NameT {
            get {
                if (_nameT != null || !Translations.IsReady) { return _nameT; }
                try {
                    string nk = NameKey;
                    if (UseParentTexts) {
                        var parent = The.Defs.Get(ParentId);
                        if (parent == null) {
                            D.Err("UseParentTexts is set to true on def that has no parent by id: {0} | {1}",
                                ParentId, Id);
                        }
                        int attempts = 10;
                        while (parent != null && attempts-- > 0) {
                            if (parent.NameKey != null) {
                                nk = parent.NameKey;
                                break;
                            } else {
                                if (parent.UseParentTexts) {
                                    parent = The.Defs.Get(parent.ParentId);
                                    continue;
                                }
                                D.Err("UseParentTexts is set to true on def that has a parent with null NameKey: {0} | {1}",
                                    ParentId, Id);
                            }
                        }
                        if (attempts < 1) {
                            D.Err("Out of attempts when trying to get parent name key! Cyclic dependency? {0}", Id);
                        }
                    }
                    if (nk == null) {
                        D.Err("NameKey is null for def: {0}", Id);
                        _nameT = $"NO_NameKey_{Id}";
                        return _nameT;
                    }
                    if (Arrays.IsEmpty(TranslationVars)) {
                        _nameT = nk.T();
                        if (_nameT.Contains("{0}")) {
                            D.Err("Def name has a variable in translations, but not in def json! {0} {1}",
                                Id, nk);
                        }
                    } else {
                        try {
                            _nameT = nk.T(TranslationVars);
                        } catch (Exception ex) {
                            D.LogEx(ex,
                                "Failed to fill in translation variables for: {0}. Lang: {1}",
                                Id, Translations.CurrentLocale);
                            // Fallback
                            _nameT = nk.T();
                        }
                    }
                } catch (Exception ex) {
                    D.LogEx(ex,
                        "Failed processing translation for {0}", NameKey);
                    _nameT = NameKey;
                    WPopup.SystemWarning("Translation likely invalid", $"Translation likely invalid: {NameKey}");
                }
                return _nameT;
            }
        }
        public bool HasDescT {
            get {
                if (descT != null) { return true; }
                var nk = UseParentTexts ? The.Defs.Get(ParentId).NameKey : NameKey;
                return $"{nk}.desc".HasT();
            }
        }
        private string descT;
        public string DescT {
            get {
                if (descT == null && Translations.IsReady) {
                    var dk = UseParentTexts ? The.Defs.Get(ParentId).DescKey : DescKey;
                    if (dk == null) {
                        dk = UseParentTexts ? The.Defs.Get(ParentId).NameKey : NameKey;
                        dk = $"{dk}.desc";
                    }
                    if (Arrays.IsEmpty(TranslationVars)) {
                        descT = dk.T();
                        if (descT.Contains("{0}")) {
                            D.Err("Def description has a variable in translations, but not in def json! {0} {1}",
                                Id, dk);
                        }
                    } else {
                        try {
                            descT = dk.T(TranslationVars);
                        } catch (Exception ex) {
                            D.LogEx(ex,
                                "Failed to fill in translation variables for desc: {0}. Lang: {1}",
                                Id, Translations.CurrentLocale);
                            // Fallback
                            descT = dk.T();
                        }
                    }
                }
                return descT;
            }
        }


        private bool skipPrice;

        public string NameWithPrice(GameState S, bool accented) {
            if (!skipPrice && S.Research.IsUnlocked(DefIdsH.ResearchEconomyStockMarket)) {
                var price = S.Market.Find(IdH);
                if (price == null) {
                    skipPrice = true;
                } else {
                    var priceText = Texts.WithSize(Texts.Green(
                        Wallet.ToValueString(
                            price.Adjusted().Value)), 70);
                    if (accented) {
                        return $"{Texts.Accented(NameT)} {priceText}";
                    } else {
                        return $"{NameT} {priceText}";
                    }
                }
            }
            if (accented) {
                return Texts.Accented(NameT);
            }
            return NameT;
        }

        public void ShowManualEntry() {
            WTooltipCodex.SpawnFor(A.S, this);
        }
        private string extendedDesc;
        private string extendedDescShort;
        public string ExtendedDesc(bool shortVersion) {
            #if CACHE_EXTENDED_DESC
            if (shortVersion && extendedDescShort != null) {
                return extendedDescShort;
            }
            if (!shortVersion && extendedDesc != null) {
                return extendedDesc;
            }
            #endif
            var sb = new StringBuilder();
            try {
                AppendModAndUnfinished(sb);
                if (!shortVersion) {
                    // sb.Append(GlyphMap.HR);
                    // sb.Append("\n");
                    if (HasDescT) {
                        sb.AppendLine(DescT);
                    }
                }

                bool canBeBuilt = false;
                if (!Researchable.IsEmptyPrereq(Researchable)) {
                    // if (!shortVersion) {
                    //     var prereq = The.Defs.Get(Researchable.Prerequisites[0]);
                    //     var link = Texts.LinkToResearch(prereq, Texts.GreySmaller(T.OpenResearch));
                    //     sb.AppendLine();
                    //     sb.Append(link);
                    // }
                    canBeBuilt = true;
                    // } else if (SkipToolListing) {
                    // sb.AppendLine();
                    // sb.Append(Texts.GreySmaller(T.DeviceLootableOnly));
                }

                if (BodyPartDef.TryForDef(IdH, out var bp)) {
                    bp.AddInfo(sb, null, 1f);
                }
                if (InventoryItemDef.TryForDef(IdH, out var ii)) {
                    ii.AddInfo(sb, 1f, true);
                }
                if (MatType != null && UpgradeRegistry.TryGetByMat(MatType.Id, out var upgrade)) {
                    upgrade.AddDefInfo(sb);
                }
                AppendSeasonalNote(sb);
                AppendConstructableDesc(sb, false, canBeBuilt);
                AppendExtractable(sb);
                AppendCraftables(sb);
                CrafterDefInfo.AppendProducts(sb, this,
                    CodexExportScope.IsActive ? int.MaxValue : maxNumIngr);
                AppendIngredientFor(sb);
                AppendRecyclable(sb);
                AddHarvestable(sb);
                AddHarvestOutput(sb);
                AddStorageInfo(sb, null);

                if (Components != null) {
                    foreach (var comp in Components) {
                        if (The.Defs.TryGetPrototype<IDefDescriptionProvider>(
                                comp.ComponentH, out var descProv)) {
                            try {
                                descProv.AddDefInfo(sb, this);
                            } catch (Exception ex) {
                                D.LogEx(ex, "Failure while loading AddDefInfo for component {0}", descProv);
                                ModRegistry.WarnAboutBrokenMod(descProv, "Failed to generate item description");
                            }
                        }
                    }
                }
                AddAdditionalInfo(sb);
                if (SkipToolListing) {
                    sb.Append(GlyphMap.BRHR);
                    sb.Append(Texts.GreySmaller(T.DeviceLootableOnly));
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failure while loading ExtendedDesc for def {0}", Id);
                ModRegistry.WarnAboutBrokenMod(this, "Failed to generate item description");
                sb.Append('\n');
                sb.AppendFormat("\nError: {0}", Texts.Red(ex.Message));
            }

            if (shortVersion) {
                extendedDescShort = sb.ToString().TrimStart();
                sb.Clear();
                return extendedDescShort;
            } else {
                extendedDesc = sb.ToString();
                sb.Clear();
                // A crutch, but I'm tired of fixing string stitching bugs
                extendedDesc = extendedDesc.Replace("\n\n\n", "\n\n");
                return extendedDesc;
            }
        }

        private void AppendSeasonalNote(StringBuilder sb) {
            if (Seasonal == null) { return; }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.WithColor(T.ResearchSeasonalLocked_V1.F(Seasonal.RangeText),
                Texts.TMPColorYellow));
        }

        private void AppendExtractable(StringBuilder sb) {
            if (MatType?.Deposit == null) { return; }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(T.AdjExtractable));
            sb.Append(T.FoundInSpace);
        }

        // Deconstruction refunds the build cost, which the Ingredient block already lists as
        // "Required for building", so only disposing is listed here
        private void AppendRecyclable(StringBuilder sb) {
            if (MatType == null) { return; }
            var disposables = Craftable.FindDisposablesFor(this);
            if (disposables == null) { return; }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(T.AdjRecyclable));
            var products = new List<Def>(disposables.Count);
            foreach (var disp in disposables) {
                if (disp.ProductDef != null && !products.Contains(disp.ProductDef)) {
                    products.Add(disp.ProductDef);
                }
            }
            sb.Append(T.RecycledByDisposing_V1.F(LinksInUnlockOrder(products, maxNumIngr)));
        }

        // Website export shows the complete list instead of truncating with +N
        private static string LinksInUnlockOrder(IReadOnlyList<Def> defs, int limit) {
            var linksSb = new StringBuilder();
            DefUnlockOrder.AppendLinks(linksSb, defs, limit);
            return linksSb.ToString();
        }
        private List<Def> GetHarvestSources() {
            List<Def> res = null;
            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsAbstract || def.IsTemplate || def.LayerId <= WorldLayer.Objects) { continue; }
                var c = def.ComponentConfigFor(ComponentIdH.Plant, false);
                if (c != null) {
                    var mats = c.GetRawMaterialsRef(PropertyIdH.HarvestOutput, false, null);
                    if (!Arrays.IsEmpty(mats)) {
                        foreach (var mat in mats) {
                            if (mat.Type == MatType) {
                                if (res == null) {
                                    res = new List<Def>();
                                }
                                res.Add(def);
                                break;
                            }
                        }
                    }
                }
            }

            return res;
        }

        private List<Def> GetConstructables() {
            List<Def> res = null;
            foreach (var def in The.Defs.Defs.Values) {
                if (def.IsAbstract || def.IsTemplate || def.LayerId > WorldLayer.Objects) { continue; }
                // The player can never place these, only find them
                if (def.SkipToolListing) { continue; }
                if (!def.IsInSeason && !A.InstantBuild) { continue; }
                var c = def.ComponentConfigFor(ComponentIdH.Constructable, false);
                if (c != null) {
                    var mats = c.GetRawMaterialsRef(PropertyIdH.Contents, false, null);
                    if (!Arrays.IsEmpty(mats)) {
                        foreach (var mat in mats) {
                            if (mat.Type == MatType) {
                                if (res == null) {
                                    res = new List<Def>();
                                }
                                res.Add(def);
                                break;
                            }
                        }
                    }
                }
            }
            return res;
        }

        private void AppendCraftables(StringBuilder sb) {
            var craftables = Craftable.FindForDef(this);
            if (craftables == null) { return; }
            bool hasCraftables = false;
            foreach (var cr in craftables) {
                // disposable
                if (Arrays.IsEmpty(cr.Ingredients)) {
                    continue;
                }
                hasCraftables = true;
                break;
            }
            if (!hasCraftables) { return; }
            var ordered = craftables.OrderBy((c) => c.ListOrder);
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(T.AdjCraftable));
            int num = 0;
            string lastRecipeHeader = null;
            foreach (var cr in ordered) {
                lastRecipeHeader = AddCraftingRecipe(cr, sb,
                    num == 0 ? null : GlyphMap.BRHR,
                    lastRecipeHeader: lastRecipeHeader);
                num++;
            }
        }


        private void AppendIngredientFor(StringBuilder sb) {
            if (MatType == null) { return; }
            var products = Craftable.FindProductsUsing(MatType);
            List<Def> crafted = null;
            if (products != null) {
                crafted = new List<Def>(products.Count);
                foreach (var p in products) {
                    if (!p.IsInSeason && !A.InstantBuild) { continue; }
                    crafted.Add(p);
                }
            }
            var built = GetConstructables();
            var hasCrafted = crafted != null && crafted.Count > 0;
            if (!hasCrafted && built == null) { return; }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(T.Ingredient));
            if (hasCrafted) {
                sb.Append(T.RequiredForCrafting);
                sb.Append(' ');
                sb.Append(LinksInUnlockOrder(crafted, maxNumIngr));
            }
            if (built != null) {
                if (hasCrafted) {
                    sb.AppendLine();
                }
                sb.Append(T.RequiredForBuilding);
                sb.Append(' ');
                sb.Append(LinksInUnlockOrder(built, maxNumIngr));
            }
        }

        public bool AppendUnlockInstructions(StringBuilder sb, bool includeName,
                bool hasTextAbove) {
            if (Researchable.IsEmptyPrereq(Researchable)) { return false; }
            if (The.R.State.Research.IsDefUnlocked(this)) { return false; }
            var researchId = Arrays.SafeGet(Researchable.Prerequisites, 0);
            var researchDef = The.Defs.Get(researchId);
            if (researchDef != null) {
                var resPath = The.R.State.Research.TreePathFor(researchDef);
                if (hasTextAbove) {
                    sb.AppendLine();
                }
                sb.Append("<style=Hint>");
                if (includeName) {
                    sb.AppendFormat("{0}: {1}. ", NameT, Texts.Red(T.ResearchLocked));
                } else {
                    sb.AppendFormat("{0}. ", Texts.Red(T.ResearchLocked));
                }
                sb.Append(T.DefUnlockTip_V1.F(resPath));
                sb.Append("</style>");
                return true;
            } else {
                D.Err("Research def null for locked researchable prerequisite: {0}. Def: {1}",
                    researchId, Id);
                return false;
            }
        }

        private void AppendModAndUnfinished(StringBuilder sb) {
            if (!IsCoreMod) {
                sb.Append(Texts.WithSize(The.ModLoader.ModTagFor(this), 70));
                sb.Append("\n");
            } if (IsUnfinished) {
                sb.Append(Texts.Bold(Texts.WithColor(
                    T.UnfinishedFeature, Texts.TMPColorRed)));
                sb.Append("\n");
            }
        }

        public void AddStorageInfo(StringBuilder sb, Obj obj) {
            var storages = CompatibleStorages(obj);
            if (Arrays.IsEmpty(storages)) { return; }
            // this is for Def description
            if (obj == null) {
                sb.Append(GlyphMap.BRHR);
                sb.Append(Texts.SubTitle(T.CompatibleStorage));
            } else {
                sb.Append(T.CompatibleStorage);
                sb.Append(": ");
            }
            var sc = storages.Count;
            for (int i = 0; i < sc; i++) {
                Def store = storages[i];
                sb.Append(store.CodexLink);
                if (i < sc - 1) {
                    sb.Append(", ");
                }
            }
        }

        // The bare "No Compatible Storage Unit" error left the player guessing which
        // storage to build, so it names the compatible ones (the list the tooltip shows)
        public string StorageUnavailableMessage(Obj obj) {
            var storages = CompatibleStorages(obj);
            if (Arrays.IsEmpty(storages)) { return T.MatStorageUnavailable; }
            // Only built on a player click, and callers may hold the shared builders
            var sb = new StringBuilder();
            sb.Append(T.CompatibleStorage);
            sb.Append(": ");
            var sc = storages.Count;
            for (int i = 0; i < sc; i++) {
                sb.Append(storages[i].CodexLink);
                if (i < sc - 1) {
                    sb.Append(", ");
                }
            }
            return $"{T.MatStorageUnavailable}\n\n{Texts.Smaller(sb.ToString())}";
        }

        public List<Def> CompatibleStorages(Obj obj) {
            var mat = MatType.FindByDef(this);
            var defs = Caches.DefList;
            defs.Clear();
            if (mat != null && obj?.Pile?.TreatAsObj != true) {
                if (MatType.CompatibleStorages == null) {
                    D.Err("Trying to AddMatInfo while MatType.CompatibleStorages are not yet initialized!");
                } else if (MatType.CompatibleStorages.TryGetValue(mat.IdH, out var storages)) {
                    defs.AddRange(storages);
                }
                if (obj != null) {
                    DefUnlockOrder.Sort(defs);
                    return defs;
                }
            }

            var storable = ComponentConfigFor(ComponentIdH.StorableObj, false);
            if (storable != null) {
                var g = storable.GetString(PropertyIdH.Group);
                var storages = The.R.Query.ObjStorageDefsForGroup.Ask(Hashes.S(g));
                defs.AddRange(storages);
            }
            DefUnlockOrder.Sort(defs);
            return defs;
        }

        public string AddCraftingRecipe(Craftable cr, StringBuilder sb,
                string separator = "\n\n", string lastRecipeHeader = null) {
            // disposable?
            if (Arrays.IsEmpty(cr.Ingredients)) {
                return lastRecipeHeader;
            }
            if (Arrays.IsEmpty(cr.Crafters)) {
                D.Err("No crafters available for craftable: {0} -> {1}", Id, cr);
                return lastRecipeHeader;
            }
            var header = T.ProducedIn_V1.F(LinksInUnlockOrder(cr.Crafters, int.MaxValue));
            if (header != lastRecipeHeader) {
                if (separator != null) {
                    sb.Append(separator);
                }
                sb.Append(header);
            }
            sb.Append("\n");
            int ingNum = 0;
            foreach (var ing in cr.Ingredients) {
                if (ing.Type?.Def == null) {
                    var msg = string.Format(
                        "Crafting Ingredient for def {0} does not have MatType or MatType does not have Def linked: {1}: Type: {2}, Type.Def: {3}",
                        this, ing, ing.Type, ing.Type?.Def);
                    D.Err(msg);
                    ModRegistry.WarnAboutBrokenMod(this, msg);
                    continue;
                }
                if (ingNum > 0) {
                    sb.Append(" + ");
                }
                sb.Append(ing.Type.Def.CodexLink);
                if (ing.StackSize > 1) {
                    sb.Append(" ");
                    sb.Append(Units.XNum(ing.StackSize));
                }
                ingNum++;
            }
            sb.AppendFormat(" + {0} = <nobr>{1}</nobr>", Texts.Accented2(Units.ElectricityWH(cr.EnergyCost,
                cr.ProductionTimeHours), bold: false),
                    Texts.Accented(NameT, bold: false));

            if (cr.OutputMultiplier > 1) {
                sb.Append(' ');
                sb.Append(Units.XNum(cr.OutputMultiplier));
            }
            return header;
        }

        private void AddHarvestable(StringBuilder sb) {
            if (MatType == null) { return; }
            List<Def> plants = GetHarvestSources();
            if (plants == null) { return; }
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            sb.Append(Texts.SubTitle(T.AdjHarvestable));
            sb.Append(T.HarvestedFrom_V1.F(LinksInUnlockOrder(plants, int.MaxValue)));
        }


        private void AddHarvestOutput(StringBuilder sb) {
            var p = ComponentConfigFor(ComponentIdH.Plant, false);
            if (p == null) { return; }
            var output = p.GetRawMaterialsRef(PropertyIdH.HarvestOutput, false, null);
            if (Arrays.IsEmpty(output)) { return; }
            sb.Append(GlyphMap.BRHR);
            sb.Append(Texts.Title(T.HarvestOutput));
            for (int i = 0; i < output.Length; i++) {
                var mat = output[i];
                if (i > 0) {
                    sb.AppendLine();
                }
                sb.Append(mat.Type.Def.CodexLink);
                sb.Append(" ");
                sb.Append(Units.XNum(mat.StackSize));
            }
        }

        private Mat[] constructedMats;
        public Mat[] ConstructedMats {
            get {
                if (constructedMats != null) { return constructedMats; }
                constructedMats = ComponentConfigFor(ComponentIdH.Constructable, false)?
                    .GetRawMaterialsCopy(PropertyIdH.Contents, false, null);
                if (constructedMats == null) {
                    return null;
                }
                for (int i = constructedMats.Length - 1; i >= 0; i--) {
                    Mat mat = constructedMats[i];
                    mat.StackSize = mat.MaxStackSize;
                    constructedMats[i] = mat;
                }
                return constructedMats;
            }
        }
        public void AddAdditionalInfo(StringBuilder sb) {
            bool needsHeader = true;
            bool addedAnything = false;
            var tr = ComponentConfigFor(ComponentIdH.TileTransform, false);
            if (tr != null) {
                if (sb.Length > 0) {
                    sb.Append(GlyphMap.BRHR);
                }
                sb.Append(Texts.SubTitle(T.NounProperties));
                needsHeader = false;
                var sizeF = string.Format("{0}x{1}", tr.GetInt(PropertyIdH.Width, 1),
                    tr.GetInt(PropertyIdH.Height, 1));
                sb.Append(T.AreaSize_V1.F(Texts.Accented(sizeF)));
                var defCover = 0;
                if (LayerId > WorldLayer.Floor)  {
                    defCover = 100;
                }
                var cover = tr.GetInt(PropertyIdH.CoverPercent, defCover);
                if (HasComponent(ComponentIdH.Door)) {
                    sb.AppendFormat(", {0}: <b>0-{1}%</b>", T.OverlayTipCover,
                        cover);
                } else {
                    sb.AppendFormat(", {0}: <b>{1}%</b>", T.OverlayTipCover,
                        cover);
                }
                addedAnything = true;
            }

            if (Components != null) {
                var subSb = new StringBuilder();
                foreach (var comp in Components) {
                    if (The.Defs.TryGetPrototype<IDefDescriptionProvider>(
                            comp.ComponentH, out var descProv)) {
                        try {
                            addedAnything = descProv.AddDefInfoAdditional(subSb, this, addedAnything);
                        } catch (Exception ex) {
                            D.LogEx(ex, "Failure while loading AddDefInfoAdditional for component {0}", descProv);
                            ModRegistry.WarnAboutBrokenMod(descProv, "Failed to generate item description");
                        }
                    }
                }
                DefPowerInfo.Append(subSb, this, addedAnything);
                if (subSb.Length > 0) {
                    if (needsHeader) {
                        if (sb.Length > 0) {
                            sb.Append(GlyphMap.BRHR);
                        }
                        sb.Append(Texts.SubTitle(T.NounProperties));
                    }
                    sb.Append(subSb);
                }
            }
        }

        public static void PrependAdditionalInfo(StringBuilder sb, bool addedAnything) {
            if (addedAnything) {
                sb.Append(", ");
            }
        }

        public void AppendConstructableDesc(StringBuilder sb, bool forBuildTool,
                bool canBeBuilt) {
            var c = ComponentConfigFor(ComponentIdH.Constructable, false);
            if (c == null) { return; }
            if (forBuildTool) {
                The.R.Sys.Inventory.LoadRemainingMaterials(Caches.MatCounts);
            }
            var contents = ConstructedMats;
            var placement = Enums.FromString<PlacementConstraint>(
                c.GetString(PropertyIdH.Placement, "OnFloor"));
            var flyingOnly = c.GetBool(PropertyIdH.FlyingOnly, false);
            if (sb.Length > 0) {
                sb.Append(GlyphMap.BRHR);
            }
            // TODO: figure out why we had this
            //sb.Append("<align=left>");
            sb.Append(Texts.SubTitle(canBeBuilt ? T.ConstructionCost : T.ContainsMaterials));
            //sb.Append("\n");//<color={0}>", Texts.TMPColorYellow);
            for (int i = 0; i < contents.Length; i++) {
                Mat item = contents[i];
                if (item.Type == null) { continue; }
                if (i > 0) {
                    sb.AppendLine();
                }
                sb.Append(item.Type.Def.CodexLink);
                sb.Append(" ");
                sb.Append(Units.XNum(item.MaxStackSize));
                if (forBuildTool) {
                    if (!Caches.MatCounts.TryGetValue(item.Type, out var cnt)) {
                        cnt = 0;
                    }
                    var cntText = T.AvailableCount_V1.F(cnt);
                    string textCol;
                    if (cnt >= item.MaxStackSize * 2) {
                        textCol = Texts.TMPColorGreen;
                    } else if (cnt < item.MaxStackSize) {
                        textCol = Texts.TMPColorRed;
                    } else {
                        textCol = null;
                    }
                    sb.AppendFormat(" ({0})", Texts.TryWithColor(cntText, textCol));
                }
            }
            //sb.Append("</color>");
            if (flyingOnly) {
                sb.Append(GlyphMap.BRHR);
                sb.Append(T.RequiredSkills);
            }
            if (flyingOnly) {
                //sb.Append(GlyphMap.BRHR);
                sb.Append(Texts.WithColor(T.ReqNavFlight, Texts.TMPColorYellow));
            }
            // sb.Append("</color>");
            if (!forBuildTool) {
                string place = null;
                switch (placement) {
                    case PlacementConstraint.Anywhere: {
                        place = T.PlacementTipAnywhere;
                        break;
                    }
                    case PlacementConstraint.InSpace: {
                        place = T.PlacementTipInspace;
                        break;
                    }
                    case PlacementConstraint.InSpaceAttached: {
                        place = T.PlacementTipInspaceattached;
                        break;
                    }
                    case PlacementConstraint.InSpaceAttachedChained: {
                        place = T.PlacementTipInspaceattachedchained;
                        break;
                    }
                }
                if (place != null) {
                    sb.AppendLine();
                    sb.Append(Texts.GreySmaller(place));
                }
            }
        }

        private void ExtendedTooltipCtrlPressed(GameState S, string prefix, bool skipCount,
                StringBuilder tooltipSB) {
            if (prefix == null && skipCount) {
                tooltipSB.AppendFormat("{0}\n{1}",
                    NameWithPrice(S, true), ExtendedDesc(false));
                return;
            }
            var hasTextAbove = AppendPrefixAndCount(S, prefix, skipCount, tooltipSB);
            if (AppendUnlockInstructions(tooltipSB, false, hasTextAbove)) {
                hasTextAbove = true;
            }
            if (hasTextAbove) {
                tooltipSB.Append(GlyphMap.BRHR);
            }
            tooltipSB.AppendFormat("{0}\n{1}",
                NameWithPrice(S, true), ExtendedDesc(false));
        }

        private bool AppendPrefixAndCount(GameState S, string prefix, bool skipCount,
                StringBuilder tooltipSB) {
            bool appended = false;
            if (prefix != null) {
                tooltipSB.Append(prefix);
                appended = true;
            }
            if (!skipCount) {
                if (appended) {
                    tooltipSB.Append(GlyphMap.BRHR);
                } else {
                    appended = true;
                }
                tooltipSB.Append(T.AvailableCount_V1.F(Texts.Accented(S.Sys.Inventory.CountOf(this)), false));
            }
            return appended;
        }
        private void ExtendedTooltipNoCtrlPressed(GameState S, string prefix, bool skipCount,
                StringBuilder tooltipSB) {
            bool hasTextAbove;
            if (prefix == null && skipCount) {
                tooltipSB.Append(NameWithPrice(S, true));
                hasTextAbove = true;
            } else {
                hasTextAbove = AppendPrefixAndCount(S, prefix, skipCount, tooltipSB);
            }

            if (hasTextAbove) {
                tooltipSB.Append(GlyphMap.BRHR);
            }
            var glyph = The.Bindings.GetControlName(Input.ActionType.CtrlModifier);
            tooltipSB.Append(Texts.Accented2(T.HoldButtonForMoreInfo_V1.F(glyph)));
            // tooltipSB.Append('.');
        }
        private readonly StringBuilder tooltipSB = new();
        [Obsolete("Use UIUtils.AddCodexTooltip instead of this")]
        public string ExtendedTooltip(GameState S, string prefix = null,
                bool alwaysFull = false, bool skipCount = false) {
            tooltipSB.Clear();
            if (alwaysFull || The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                ExtendedTooltipCtrlPressed(S, prefix, skipCount, tooltipSB);
            } else {
                ExtendedTooltipNoCtrlPressed(S, prefix, skipCount, tooltipSB);
            }
            var str = tooltipSB.ToString();
            tooltipSB.Clear();
            return str;
        }

        public void AfterLoad() {
            if (Researchable?.IsTreeNode == true) {
                Researchable.Def = this;
            }
            if (!Researchable.IsEmptyPrereq(Researchable)) {
                for (int i = Researchable.Prerequisites.Length - 1; i >= 0; i--) {
                    string p = Researchable.Prerequisites[i];
                    var r = The.Defs.TryGet(p);
                    // Cannot remap researchable tree nodes because we don't
                    // know tier, datoid costs, etc
                    if (r == null && !Researchable.TreeNode(Researchable)
                            && ResearchRemap.Try(p, out var newP,
                                IsCoreMod ? $"For Def: {Id}" : $"For Mod Def: {Id} ({ModId})")) {
                        Researchable.Prerequisites[i] = newP;
                        r = The.Defs.TryGet(newP);
                        p = newP;
                    }
                    if (r != null) {
                        Tier = Math.Max(r.Tier, Tier);
                    } else {
                        // if experimental is disabled it's fine, as some nodes may be missing
                        D.Err("Researchable prerequisite def not found: {0} -> {1}", Id, p);
                    }
                }
            }
            if (Arrays.IsEmpty(Components)) { return; }
            componentsByType.Clear();
            if (Id.StartsWith("Templates/")) {
                IsTemplate = true;
            }
            foreach (var c in Components) {
                if (!componentsByType.TryAdd(c.ComponentH, c)) {
                    D.Err("Duplicate component: {0} -> {1}", Id, c.Component);
                }
                var proto = The.Defs.GetPrototype(c, null);
                if (proto is IAfterInitDef i) {
                    i.AfterInitDef(this, c);
                }
                if (!IsAbstract && !IsTemplate) {
                    if (proto is ConstructableComp) {
                        IsConstructable = true;
                        IsPlacementInSpace =
                            ConstructableComp.IsPlacementInSpace(
                                ConstructableComp.PlacementConstraintFor(c));
                    }
                }
            }
        }

        ///<summary>This is internal for reading from JSON. Use Preview/PreviewSprite instead!</summary>
        ///<see cref="Preview"/>
        ///<see cref="PreviewSprite"/>
        public string PreviewIcon;
        public string ParentId;
        public int LayerId;
        public string Layer;
        // A field in every build: mods bind to the release shape (see Clock.Ticks)
        [NonSerialized] public MatType MatType;
        public bool IsBeing;
        public string[] TranslationVars;
        public ComponentConfig[] Components;
        public static IComparer<Def> NameComparer = Comparer<Def>.Create((a, b) =>
            string.Compare(a?.NameT, b?.NameT, StringComparison.Ordinal));
        private readonly Dictionary<int, ComponentConfig> componentsByType = new();


        /// <summary>Warning: Will copy references to ComponentConfig serialized properties!</summary>
        public Def Copy() {
            var def = new Def {
                IsAbstract = IsAbstract,
                IsUnfinished = IsUnfinished,
                Tier = Tier,
                ResearchValue = ResearchValue,
                WealthValueMul = WealthValueMul,
                IsImportant = IsImportant,
                IgnoreMarket = IgnoreMarket,
                Seasonal = Seasonal?.Copy(),
                Researchable = Researchable?.Copy(),
                Spawnable = Spawnable?.Copy(),
                LayerId = LayerId,
                ToolPath = ToolPath,
                BuildGroup = BuildGroup,
                IsCoreMod = IsCoreMod,
                ModId = ModId,
                Layer = Layer,
                TranslationVars = TranslationVars,
                NameKey = NameKey,
                DescKey = DescKey,
                PreviewIcon = PreviewIcon,
                SkipToolListing = SkipToolListing,
                UseParentTexts = UseParentTexts,
            };

            if (Components != null) {
                def.Components = new ComponentConfig[Components.Length];
                for (int i = 0; i < Components.Length; i++) {
                    def.Components[i] = Components[i].Copy();
                }
            }

            // def.componentsByType.Clear();
            return def;
        }

        public bool HasComponent(int nameH) {
            if (componentsByType.TryGetValue(nameH, out _)) {
                return true;
            }
            return false;
        }

        public ComponentConfig ComponentConfigFor(int nameH, bool warn = true) {
            if (nameH == 0) {
                D.Err("Trying to get component with 0 idH");
                return null;
            }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                warn = false;
            }
            #endif
            if (Components == null) {
                if (warn) {
                    D.Warn("No components defined for {0} on def: {1}",
                        ComponentIdH.ResolveName(nameH) , Id);
                }
                return null;
            }
            if (componentsByType.TryGetValue(nameH, out var res)) {
                return res;
            }
            if (!Ready.Core) {
                // This is expected during load / initialize phase
                for (int i = Components.Length - 1; i >= 0; i--) {
                    ComponentConfig cc = Components[i];
                    if (nameH == cc.ComponentH) {
                        return cc;
                    }
                }
            }
            if (warn) {
                D.Warn("Missing component config for {0} on def: {1}",
                    ComponentIdH.ResolveName(nameH), Id);
            }
            return null;
        }

        public Sprite PreviewSprite {
            get {
                if (previewSprite != null) { return previewSprite; }

                // Cannot do that, because all research definitions are abstract!
                //if (IsAbstract) { return null; }

                if (!string.IsNullOrWhiteSpace(PreviewIcon)) {
                    previewSprite = Images.Sprite(PreviewIcon);
                    return previewSprite;
                }

                if (Components == null) {
                    D.Err("Components are null while trying to get PreviewSprite! {0}", Id);
                    return null;
                }

                for (int i = 0; i < Components.Length; i++) {
                    var c = Components[i];
                    if (c == null) {
                        D.Err("Component #{1} is null while trying to get PreviewSprite! {0}", Id, i);
                        continue;
                    }
                    switch (c.Component) {
                        case "TileGraphics": {
                            var sd = TileGraphicsComp.IconSDFor(Id, c);
                            if (sd == null) {
                                D.Err("IconSd is null for {0}", Id);
                                return null;
                            }
                            previewSprite = sd.Sprite;
                            return previewSprite;
                        }
                        case "BeingGraphics": {
                            // FIXME make it proper
                            previewSprite = Images.Sprite(IconId.CPreviewHuman01);
                            return previewSprite;
                        }
                        case "ObjGraphics": {
                            previewSprite = Images.Sprite(ObjGraphicsComp.IconFor(c));
                            return previewSprite;
                        }
                    }
                }
                D.Err("No preview sprite for {0}", this);
                return null;
            }
        }

        public string Preview {
            get {
                if (preview != null) { return preview; }

                // Cannot do that, because all research definitions are abstract!
                //if (IsAbstract) { return null; }

                if (!string.IsNullOrWhiteSpace(PreviewIcon)) {
                    preview = PreviewIcon;
                    return preview;
                }

                for (int i = 0; i < Components.Length; i++) {
                    var c = Components[i];
                    switch (c.Component) {
                        case "TileGraphics": {
                            var sprite = TileGraphicsComp.IconSDFor(Id, c);
                            if (sprite != null) {
                                preview = sprite.Id;
                                return preview;
                            }
                            return IconId.WMissing;
                        }
                        case "BeingGraphics": {
                            // FIXME make it proper
                            preview = IconId.CPreviewHuman01;
                            return preview;
                        }
                        case "ObjGraphics": {
                            preview = ObjGraphicsComp.IconFor(c);
                            return preview;
                        }
                    }
                }
                D.Err("No preview for {0}", this);
                return null;
            }
        }

        public override string ToString() {
            if (IsCoreMod) {
                return $"Def[{Id}]";
            } else {
                return $"Def[{Id}|Mod:{ModId}]";
            }
        }
    }
}
