using System;
using System.Buffers;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.AI.Combat;
using System.Diagnostics;
using Game.Data.Economy;
using System.Text;
using Game.Input;
using Game.Systems.Mental;
using Game.Systems.Crafting;
using System.Linq;
using Unity.Mathematics;

namespace Game.Systems.BodyParts {
    public sealed class BodyPartDef : IAbilitySourceItem, INeedSourceItem {
        public static readonly Dictionary<int, BodyPartDef> All = new();
        public static readonly Dictionary<int, BodyPartDef> byDef = new();

        [ExecuteOnReload]
        private static void ClearStatics() {
            All.Clear();
            byDef.Clear();
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        public string NameT;
        public string DescT;
        /// <summary>
        /// Optional translation key overrides, same idea as Def.NameKey / Def.DescKey. Without
        /// them the keys are derived from Id, so moving a part to a different slot folder
        /// (Appendage/LegCrawler -> Leg/LegCrawler) would orphan its existing translations.
        /// Set them to keep the old keys. An explicit key also wins over the linked Def's.
        /// </summary>
        public string NameKey;
        public string DescKey;
        public string Id;
        public int IdH;
        public string DefId;
        public Def Def;
        public BodyPartType SlotType;
        public BodyPartFlags Flags;
        public bool IsIndestructible => (Flags & BodyPartFlags.Indestructible) == BodyPartFlags.Indestructible;
        public bool IsBiological => (Flags & BodyPartFlags.Biological) == BodyPartFlags.Biological;
        public bool NeverGenerate => (Flags & BodyPartFlags.NeverGenerate) == BodyPartFlags.NeverGenerate;
        public bool HoldsMind => (Flags & BodyPartFlags.HoldsMind) == BodyPartFlags.HoldsMind;
        public bool SupportsMLBooth => (Flags & BodyPartFlags.SupportsMLBooth) == BodyPartFlags.SupportsMLBooth;
        public bool SupportsMindTransfer => (Flags & BodyPartFlags.SupportsMindTransfer) == BodyPartFlags.SupportsMindTransfer;
        public string SlotTypeId;
        public string IconId => Def?.Preview;
        [JsonEnum(typeof(Game.Systems.BodyParts.BodyPartCompatibility))]
        public string Compatibility;
        public int Tier;
        public float CombatStrengthBonus;
        private static Curve qualityStatMulCurve;
        public static Curve QualityStatMulCurve => qualityStatMulCurve ??= CurveDef.FastGet(CurveIdH.Beings_BodyPartQuality_StatMul);
        private static readonly Curve qualityResistanceCurve = new float[] {
            0f, 0.5f,
            0.25f, 0.6f,
            0.5f, 0.8f,
            0.95f, 1f,
            1.0f, 1.1f,
        };
        public BodyPartCompatibility PartCompatibility;
        public string[] CompatibleBodySizes;
        public BodySize[] CompatibleSizes;
        /// <summary>
        /// Species StatFlags (Humanoid, Animal, ...) this part fits when it is not native to
        /// the patient. Any one of them matching is enough; null means no restriction. Size and
        /// flags are separate gates because they answer different questions: size is whether the
        /// part can carry the frame, flags are whether it belongs on that kind of frame at all.
        /// </summary>
        [JsonEnum(typeof(Game.Systems.Stats.StatFlags))]
        public string[] CompatibleSpeciesFlags;
        public StatFlags CompatibleStatFlags;
        public string[] TargetSpecies;
        public string[] AddsAbilities;
        public string[] AddsNeeds;
        /// <summary>
        /// When this body part is hit by an attack, how much damage is required to inflict an irreparable / fatal wound
        /// </summary>
        public float ResistanceBase;
        /// <summary>
        /// ResistanceBase overrides for certain attack types
        /// </summary>
        public DamageResistance[] Resistances;
        public Dictionary<DamageType, float> DamageResistances;
        /// <summary>
        /// item1 is stat id hash
        /// </summary>
        public (int, StatModifier)[] StatModifiers;
        public ComponentConfig[] AddsStatModifiers;
        public ComponentConfig[] ProvidesStats;
        public StatSourceData[] StatSources;
        public ComponentConfig[] AddsAttackMethods;
        public AttackMethodData[] AttackMethods;
        /// <summary>
        /// The part inherits all type flags but additionally can have more
        /// </summary>
        [JsonEnum(typeof(Game.Systems.BodyParts.BodyPartFlags))]
        public string[] PartFlags;
        public string ItemName => NameT;
        /// <summary>
        /// LookPartTypeDef id this part drives when installed (e.g. "Tail"). Null for the vast
        /// majority of body parts, which have no visual presence of their own. See
        /// Docs/Architecture/Systems/BeingLooks.md ("Body-part-driven look parts").
        /// </summary>
        public string LooksPart;
        /// <summary>
        /// Sprite id applied to the LooksPart look part while this part is installed.
        /// "[BODY_TYPE]" is replaced with the being's body-type tag (same convention as
        /// equipment, see BeingLooks.SpriteForBodyType); facing/_H/_1 suffixes are applied by
        /// the normal render path.
        /// </summary>
        public string LooksSprite;
        [System.NonSerialized] private LookPartTypeDef looksPartType;
        [System.NonSerialized] private bool looksPartResolved;
        /// <summary>
        /// Resolved LooksPart, lazily looked up (LookPartTypeDef registers at SelfInit.Finalize,
        /// after body part defs load, so resolving eagerly at load time would be unsafe).
        /// </summary>
        public LookPartTypeDef LooksPartType {
            get {
                if (!looksPartResolved) {
                    looksPartResolved = true;
                    looksPartType = string.IsNullOrEmpty(LooksPart)
                        ? null : LookPartTypeDef.Get(LooksPart);
                }
                return looksPartType;
            }
        }

        public static void LoadKnown() {
            if (All.Count != 0) { return; }
            D.Warn("Loading body part defs...");
            BodyPartType.LoadKnown();
            var types = The.ModLoader.LoadObjects<BodyPartDef>("Config/Body/Parts");
            foreach (var def in types) {
                def.IdH = Hashes.S(def.Id);
                if (!All.TryAdd(def.IdH, def)) {
                    D.Err("Cannot add body part: {0}. Already added!", def.Id);
                }
                def.SlotType = BodyPartType.Get(def.SlotTypeId);
                if (def.Compatibility == null) {
                    D.Err("Compatibility not defined in body part def: {0}", def.Id);
                    def.Compatibility = "TargetSpecies";
                }
                def.PartCompatibility = Enums.FromString<BodyPartCompatibility>(def.Compatibility);
                def.CompatibleSizes = Enums.FromStringArray<BodySize>(def.CompatibleBodySizes, def.Id);
                def.CompatibleStatFlags = Enums.FlagsFromStringArray_UInt<StatFlags>(
                    def.CompatibleSpeciesFlags, def.Id);
                if (def.PartCompatibility == BodyPartCompatibility.TargetSpecies) {
                    if (Arrays.IsEmpty(def.TargetSpecies)) {
                        D.Err("Invalid body part def: {0}. Compatibility is TargetSpecies, but TargetSpecies is blank!", def.Id);
                    }
                    //can't check species, not loaded yet
                }
                var tId = Strings.SlashesToDots(def.Id);
                def.NameT = (def.NameKey ?? $"bodypart.{tId}").T();
                def.DescT = (def.DescKey ?? $"bodypart.{tId}.desc").TOrNull();
                def.StatSources = StatSourceData.Deserialize(def.ProvidesStats, def);
                def.StatModifiers = StatModifier.Deserialize(def.AddsStatModifiers, def.NameT, def);
                def.AttackMethods = AttackMethodData.Deserialize(def.AddsAttackMethods);
                if (!Arrays.IsEmpty(def.Resistances)) {
                    def.DamageResistances = new();
                    foreach (var d in def.Resistances) {
                        if (Enums.TryFromString<DamageType>(d.DamageType, out var type)) {
                            def.DamageResistances.Add(type, d.Resistance);
                        } else {
                            D.Err("Invalid DamageType ({0}) in Resistances of BodyPart: {1}:",
                                d.DamageType, def.Id);
                        }
                    }

                }
                def.Flags = def.SlotType.Flags;
                if (!Arrays.IsEmpty(def.PartFlags)) {
                    foreach (var f in def.PartFlags) {
                        var fen = Enums.FromString<BodyPartFlags>(f);
                        def.Flags |= fen;
                    }
                }
                if (def.PartCompatibility == BodyPartCompatibility.Biological) {
                    def.Flags |= BodyPartFlags.Biological;
                }
            }
            BodyConfiguration.LoadKnown();
            Ready.When(WhenH.CoreLoaded1, LinkDefs);
        }

        public static bool HasForDef(int defIdH) {
            return byDef.ContainsKey(defIdH);
        }

        public static bool TryForDef(int defIdH, out BodyPartDef def) {
            return byDef.TryGetValue(defIdH, out def);
        }

        private static void LinkDefs() {
            // D.Warn("Linking BodyPartDef to Def: {0}", All.Count);
            foreach (var def in All.Values) {
                // D.Warn("Linking def: {0}", def.Id);
                if (def.PartCompatibility == BodyPartCompatibility.TargetSpecies) {
                    if (!Arrays.IsEmpty(def.TargetSpecies)) {
                        bool allBiological = true;
                        foreach (var sp in def.TargetSpecies) {
                            var species = Species.Get(sp);
                            if (species == null) {
                                D.Err("Incorrect species defined in body part def: {0} -> {1}", def.Id, sp);
                            } else {
                                if (!species.StatFlags.HasFlag(StatFlags.Biological)) {
                                    allBiological = false;
                                }
                            }
                        }
                        if (allBiological) {
                            def.Flags |= BodyPartFlags.Biological;
                        }
                    }
                }
                if (def.DefId == null) {
                    def.DefId = $"Obj/Parts/{def.Id}";
                }
                def.Def = The.Defs.TryGet(def.DefId);
                if (def.Def == null) {
                    D.Err("DefId is null for body part: {0}", def.Id);
                    CreateTemplateFor(def);
                } else {
                    byDef[def.Def.IdH] = def;
                    // An explicit key on the part wins; otherwise the linked Def names it.
                    if (def.NameKey == null) {
                        def.NameT = def.Def.NameT;
                    }
                    if (def.DescKey == null) {
                        def.DescT = def.Def.DescT;
                    }
                }
            }
            LinkCraftables();
        }

        private static void LinkCraftables() {
            foreach (var def in All.Values) {
                // not craftable
                if (def.Tier == 0) { continue; }
                var craftables = Craftable.FindForDef(def.Def);
                if (craftables != null) { continue; }
                // CreateCraftableTemplateFor(def);
            }
        }

        public static bool EnsureMarketItems() {
            bool created = false;
            foreach (var def in All.Values) {
                if (def.Def.IgnoreMarket) { continue; }
                D.Warn("Ensuring market item for {0}", def.Id);
                if (MarketItem.CFDs.TryGetValue(def.IdH, out var mi)) {
                    D.Warn("Item exists for def {0}: {1}", def.Id, mi);

                } else {
                    created = true;
                    // CreateMarketItemTemplate(def);
                }
            }
            return !created;
        }

        [Conditional("UNITY_EDITOR")]
        private static void CreateMarketItemTemplate(BodyPartDef def) {
            bool isBio = def.Flags.HasFlag(BodyPartFlags.Biological);
            string tplName;
            if (isBio) {
                tplName = "BodyPartOrganic.tpl";
            } else {
                tplName = "BodyPartRobotic.tpl";
            }
            var tplFile = The.ModLoader.ModFile("Core", $"Config/Economy/MarketItems/BodyParts/{tplName}");
            var tpl = Files.ReadText(tplFile);

            tpl = tpl.Replace("{{id}}", def.Def.Id);
            var folder = def.Id.Split("/")[0];
            var targetDir = The.ModLoader.ModFile("Core", $"Config/Economy/MarketItems/BodyParts/{folder}");
            Files.EnsureDir(targetDir);
            var targetFile = The.ModLoader.ModFile("Core", $"Config/Economy/MarketItems/BodyParts/{def.Id}.json");
            Files.WriteText(targetFile, tpl);
            D.Warn("Wrote def: {0} -> {1}", targetFile, tpl);
        }

        [Conditional("UNITY_EDITOR")]
        private static void CreateCraftableTemplateFor(BodyPartDef def) {
            var tplFile = The.ModLoader.ModFile("Core", "Config/Craftable/BodyParts/Template.tpl");
            var tpl = Files.ReadText(tplFile);

            string crafterType;
            bool isBio = def.Flags.HasFlag(BodyPartFlags.Biological);
            if (isBio) {
                crafterType = $"Organics{def.Tier}";
            } else {
                crafterType = $"Robotics{def.Tier}";
            }
            string productDefId = def.Def.Id;
            int productionTimeHours = def.Tier * 12;
            int energyCost = def.Tier * 50;


            tpl = tpl.Replace("{{crafterType}}", crafterType);
            tpl = tpl.Replace("{{productDefId}}", productDefId);
            tpl = tpl.Replace("{{timeHours}}", productionTimeHours.ToString());
            tpl = tpl.Replace("{{energyCost}}", energyCost.ToString());
            tpl = tpl.Replace("{{ingredients}}",
                GetIngredientsForCraftable(def.Tier, isBio));

            var folder = def.Id.Split("/")[0];
            var targetDir = The.ModLoader.ModFile("Core", $"Config/Craftable/BodyParts/{folder}");
            Files.EnsureDir(targetDir);
            var targetFile = The.ModLoader.ModFile("Core", $"Config/Craftable/BodyParts/{def.Id}.json");
            Files.WriteText(targetFile, tpl);
            D.Err("Wrote craftable def: {0} -> {1}", targetFile, tpl);
        }

        private static string GetIngredientsForCraftable(int tier, bool isBio) {
            string prefix = isBio ? "Organic" : "Robotic";
            var ingTpl = The.ModLoader.ModFile("Core", $"Config/Craftable/BodyParts/Ingredients{prefix}{tier}.tpl");
            var tpl = Files.ReadText(ingTpl);
            return tpl;
        }

        [Conditional("UNITY_EDITOR")]
        private static void CreateTemplateFor(BodyPartDef def) {
            var tplFile = The.ModLoader.ModFile("Core", "Definitions/Obj/Parts/Template.tpl");
            var tpl = Files.ReadText(tplFile);
            string parentDefId;
            string researchId = null;
            string graphicId;
            var tier = def.Tier;
            if (def.Flags.HasFlag(BodyPartFlags.Biological)) {
                parentDefId = "BasePartOrganic";
                switch (tier) {
                    case 1: researchId = DefIds.ResearchLifeSupportGrowingPartsBasic; break;
                    case 2: researchId = DefIds.ResearchLifeSupportGrowingPartsImproved; break;
                    case 3: researchId = DefIds.ResearchLifeSupportGrowingPartsAdvanced; break;
                }
                switch (tier) {
                    case 1: graphicId = GraphicsId.ObjBodyPartsOrganic1; break;
                    case 2: graphicId = GraphicsId.ObjBodyPartsOrganic2; break;
                    case 3: graphicId = GraphicsId.ObjBodyPartsOrganic3; break;
                    default: graphicId = GraphicsId.ObjBodyPartsOrganic; break;
                }
            } else {
                parentDefId = "BasePartRobotic";
                switch (tier) {
                    case 1: researchId = DefIds.ResearchRoboticsPartsBasic; break;
                    case 2: researchId = DefIds.ResearchRoboticsPartsImproved; break;
                    case 3: researchId = DefIds.ResearchRoboticsPartsAdvanced; break;
                }
                switch (tier) {
                    case 1: graphicId = GraphicsId.ObjBodyPartsRobotic1; break;
                    case 2: graphicId = GraphicsId.ObjBodyPartsRobotic2; break;
                    case 3: graphicId = GraphicsId.ObjBodyPartsRobotic3; break;
                    default: graphicId = GraphicsId.ObjBodyPartsRobotic; break;
                }
            }

            var tId = Strings.SlashesToDots(def.Id);
            var nameKey = $"bodypart.{tId}";
            tpl = tpl.Replace("{{parentDefId}}", parentDefId);
            tpl = tpl.Replace("{{partDefId}}", def.Id);
            tpl = tpl.Replace("{{nameKey}}", nameKey);
            tpl = tpl.Replace("{{graphicId}}", graphicId);
            if (researchId != null) {
                var resTplFile = The.ModLoader.ModFile("Core", "Definitions/Obj/Parts/Researchable.tpl");
                var research = Files.ReadText(resTplFile);
                research = research.Replace("{{researchId}}", researchId);
                tpl = tpl.Replace("{{researchable}}", research);
            } else {
                tpl = tpl.Replace("{{researchable}}", "").Replace("\n\n", "\n");
            }
            var folder = def.Id.Split("/")[0];

            var targetDir = The.ModLoader.ModFile("Core", $"Definitions/Obj/Parts/{folder}");
            Files.EnsureDir(targetDir);
            var targetFile = The.ModLoader.ModFile("Core", $"Definitions/Obj/Parts/{def.Id}.json");
            Files.WriteText(targetFile, tpl);
            D.Warn("Wrote def: {0} -> {1}", targetFile, tpl);
        }

        public static BodyPartDef Get(string type) {
            return Get(type, Hashes.S(type));
        }

        public static BodyPartDef Get(string type, int typeH) {
            if (All.TryGetValue(typeH, out var part)) {
                return part;
            }
            D.Err("BodyPartDef with id {0} (hash {1}) is not defined! See Config/Body/Parts folder",
                    type, typeH);
            return null;
        }
        public static BodyPartDef Get(int type) {
            if (All.TryGetValue(type, out var part)) {
                return part;
            }
            D.Err("BodyPartDef with idHash {0} is not defined! See Config/Body/Parts folder",
                    type);
            return null;
        }
        public void AddInfo(StringBuilder sb, Species species, float quality,
                bool isExtracted = true, bool full = true) {
            // when part is installed, we will not add BRHR and won't show
            bool needsBR = false;
            var addBRHR = isExtracted;
            if (full) {
                if (isExtracted) {
                    AddInfoCompatibility(sb, species, isExtracted);
                }
                needsBR = AddInfoAbilties(sb, species, addBRHR, needsBR);
                var addsNeeds = AddsNeeds;
                AddInfoNeeds(sb, species, addBRHR, ref needsBR, ref addsNeeds);
            }
            StatsBeing statsBeing = AddInfoStats(sb, species, quality, addBRHR, ref needsBR);
            AddInfoStatModifiers(sb, species, addBRHR, ref needsBR, ref statsBeing);
        }

        private void AddInfoCompatibility(StringBuilder sb, Species sp, bool isExtracted) {
            if (isExtracted) {
                // for non-extracted parts we already see where the part is installed
                if (sb.Length > 0) {
                    sb.Append(GlyphMap.BRHR);
                }
                sb.Append(Texts.SubTitle($"{T.NounBodyPart} {Texts.GreyParentheses(SlotType.NameT)}"));
                // sb.AppendLine(Texts.Accented(SlotType.NameT, false));
            }
            if (SlotType.IdH == BodyPartSlotIdH.Body && sp != null) {
                sb.AppendFormat("{0}: {1}\n", T.NounBodySize, Texts.Accented(
                    $"body.size.{sp.BodySize}".T(), false));
            } else {
                if (!Arrays.IsEmpty(CompatibleBodySizes)) {
                    Caches.StringList2.Clear();
                    foreach (var sizeStr in CompatibleBodySizes) {
                        var size = $"body.size.{sizeStr}".T();
                        Caches.StringList2.Add(Texts.Accented(size, false));
                    }
                    sb.Append(T.CompatibleBodySizes);
                    sb.Append(": ");
                    sb.AppendJoin(", ", Caches.StringList2);
                    sb.AppendLine();
                } else {
                    sb.AppendFormat("{0}: {1}\n", T.CompatibleBodySizes, Texts.Accented(T.All, false));
                }
            }
            if (!Arrays.IsEmpty(CompatibleSpeciesFlags)) {
                Caches.StringList2.Clear();
                foreach (var flagId in CompatibleSpeciesFlags) {
                    // Reuses the nav.perm.* strings, the only existing pair that already
                    // translates both "Humanoid" and "Animal" as plain species categories.
                    Caches.StringList2.Add(Texts.Accented(
                        $"nav.perm.{flagId.ToLowerInvariant()}".T(), false));
                }
                sb.Append(T.FitsOn);
                sb.Append(": ");
                sb.AppendJoin(", ", Caches.StringList2);
                sb.AppendLine();
            }
            sb.Append(T.BodyPartSpeciesCompatibility);
            sb.Append(": ");
            // TODO Add species compatibility
            switch (PartCompatibility) {
                case BodyPartCompatibility.TargetSpecies: {
                    Caches.StringList2.Clear();
                    foreach (var speciesId in TargetSpecies) {
                        var species = Species.Get(speciesId);
                        if (species != null) {
                            Caches.StringList2.Add(Texts.Accented(species.Name, false));
                        }
                    }
                    sb.AppendJoin(", ", Caches.StringList2);
                    break;
                }
                case BodyPartCompatibility.Any: {
                    sb.Append(Texts.Accented(T.All, false));
                    break;
                }
                case BodyPartCompatibility.Biological: {
                    sb.Append(Texts.Accented(T.SpeciesTypeBiological, false));
                    break;
                }
                case BodyPartCompatibility.Robotic: {
                    sb.Append(Texts.Accented(T.SpeciesTypeRobotic, false));
                    break;
                }
                default: {
                    D.Err("Unhandled body part compatibility: {0}", PartCompatibility);
                    sb.Append(Texts.Accented(PartCompatibility.ToString(), false));
                    break;
                }
            }
            if (!isExtracted) {
                sb.AppendLine();
            }
        }

        private void AddInfoStatModifiers(StringBuilder sb, Species species,
                bool addBRHR, ref bool needsBR, ref StatsBeing statsBeing) {
            var statModifiers = StatModifiers;
            if (HoldsMind && species != null && !Arrays.IsEmpty(species.StatModifiers)) {
                var statmods = new List<(int, StatModifier)>();
                statmods.AddRange(species.StatModifiers);
                if (StatModifiers != null) {
                    statmods.AddRange(StatModifiers);
                }
                statModifiers = statmods.ToArray();
            }
            if (statModifiers != null) {
                statsBeing ??= new StatsBeing();
                bool addedSMHeader = false;
                foreach (var sm in statModifiers) {
                    var statId = sm.Item1;
                    var smod = sm.Item2;
                    Stat found = null;
                    foreach (var stat in statsBeing.AllStats) {
                        if (stat.Id == statId) {
                            found = stat;
                            break;
                        }
                    }
                    if (found == null) {
                        D.Err("Couldn't find stat for body part stat mod: {0} | {1}",
                            Id, StatIdH.ResolveName(statId));
                        continue;
                    }

                    if (!addedSMHeader) {
                        addedSMHeader = true;
                        if (addBRHR) {
                            sb.Append(GlyphMap.BRHR);
                        } else if (needsBR) {
                            sb.AppendLine();
                        }
                        sb.Append(T.ModifiesStats);
                        sb.Append(": ");
                        needsBR = true;
                    }

                    found.UpdateBase(0);
                    // sb.AppendFormat("\n - {0}:", Stat.NameFor(found));
                    smod.AppendInfo(sb, found, true, null, subItem: true, subItemGrey: false);
                }
            }
        }

        public StatsBeing AddInfoStats(StringBuilder sb, Species species, float quality,
                bool addBRHR, ref bool needsBR) {
            StatsBeing statsBeing = null;
            var statSources = StatSources;
            if (HoldsMind && species != null && !Arrays.IsEmpty(species.StatSources)) {
                var stats = new List<StatSourceData>();
                stats.AddRange(species.StatSources);
                if (StatSources != null) {
                    stats.AddRange(StatSources);
                }
                statSources = stats.ToArray();
            }
            if (statSources != null) {
                Caches.StringList.Clear();
                statsBeing ??= new StatsBeing();
                foreach (var ss in statSources) {
                    Stat found = null;
                    foreach (var stat in statsBeing.AllStats) {
                        if (stat.Id == ss.StatId) {
                            found = stat;
                            break;
                        }
                    }
                    if (found == null) {
                        D.Err("Couldn't find stat for body part stat source: {0} | {1}",
                            Id, StatIdH.ResolveName(ss.StatId));
                        continue;
                    }

                    var amt = BodyPartStatMath.WithQuality(ss, quality);

                    found.UpdateBase(amt);
                    Caches.StringList.Add(found.StatString());
                }
                if (Caches.StringList.Count > 0) {
                    if (addBRHR) {
                        sb.Append(GlyphMap.BRHR);
                    } else if (needsBR) {
                        sb.AppendLine();
                    }
                    needsBR = true;
                    var atQ = T.AtQuality_V1.F(Units.Percentage(quality, false));
                    atQ = Texts.GreyParenthesesSmaller(atQ);
                    sb.Append(Texts.SubTitle($"{T.ProvidesStats} {atQ}"));
                    Caches.StringList.Sort();
                    var list = Caches.StringList;
                    for (int i = 0; i < list.Count; i++) {
                        var stat = list[i];
                        sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, stat);
                        if (i < list.Count - 1) {
                            sb.AppendLine();
                        }
                    }
                }
            }

            return statsBeing;
        }

        public static float AdjustAmountWithStateMul(float amt, float state, float stateMul) {
            // same as 1f
            if (stateMul == 0f) {
                return amt * state;
            }
            // no effect
            if (stateMul <= 0.01f) {
                return amt;
            }
            var affAmt = amt * state;
            return math.lerp(amt, affAmt, stateMul);
        }

        private void AddInfoNeeds(StringBuilder sb, Species species, bool addBRHR,
                ref bool needsBR, ref string[] addsNeeds) {
            if (HoldsMind && species != null && !Arrays.IsEmpty(species.AddsNeeds)) {
                var needs = new List<string>();
                needs.AddRange(species.AddsNeeds);
                if (AddsNeeds != null) {
                    needs.AddRange(AddsNeeds);
                }
                addsNeeds = needs.ToArray();
            }
            if (addsNeeds != null) {
                Caches.StringList.Clear();
                foreach (var n in addsNeeds) {
                    var need = NeedType.Get(n);
                    if (need != null && !need.Need.IsInternal) {
                        Caches.StringList.Add(Texts.Accented(need.NameT, false));
                    }
                }
                if (Caches.StringList.Count > 0) {
                    if (addBRHR) {
                        sb.Append(GlyphMap.BRHR);
                    } else if (needsBR) {
                        sb.AppendLine();
                    }
                    needsBR = true;
                    sb.AppendLine();
                    sb.Append(Texts.SubTitle(T.AddsNeeds));
                    Caches.StringList.Sort();
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(' ');
                    sb.AppendJoin(", ", Caches.StringList);
                }
            }
        }

        private bool AddInfoAbilties(StringBuilder sb, Species species,
                bool addBRHR, bool needsBR) {
            var addsAbs = AddsAbilities;
            if (HoldsMind && species != null && !Arrays.IsEmpty(species.AddsAbilities)) {
                var abs = new List<string>();
                abs.AddRange(species.AddsAbilities);
                if (AddsAbilities != null) {
                    abs.AddRange(AddsAbilities);
                }
                addsAbs = abs.ToArray();
            }
            if (addsAbs != null) {
                Caches.StringList.Clear();
                foreach (var ab in addsAbs) {
                    var ability = Ability.Get(ab);
                    if (ability != null && !ability.IsInternal) {
                        Caches.StringList.Add(Texts.Accented(ability.NameT, false));
                    }
                }
                if (Caches.StringList.Count > 0) {
                    needsBR = true;
                    if (addBRHR) {
                        sb.Append(GlyphMap.BRHR);
                    } else if (needsBR){
                        sb.AppendLine();
                    }
                    sb.Append(Texts.SubTitle(T.ProvidesAbilities));
                    Caches.StringList.Sort();
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(' ');
                    sb.AppendJoin(", ", Caches.StringList);
                }
            }

            return needsBR;
        }

        public float DamageResistanceFor(DamageType type, float quality) {
            float res;
            if (DamageResistances == null || !DamageResistances.TryGetValue(
                        type, out res)) {
                res = ResistanceBase;
            }

            res *= qualityResistanceCurve.Evaluate(quality);

            return res;
        }

        /// <summary>
        /// Whether this part belongs on that species natively. A part that names no TargetSpecies
        /// is species agnostic by design - organs are graded by body size rather than by species -
        /// so it is native wherever it fits at all. Deliberately not the same as the isVanilla out
        /// of IsCompatibleWith: that answers the narrower "does this part name this species", and
        /// it has to stay false here or the CompatibleSizes gate would stop applying and a
        /// Heart/HeartLarge would fit a cat.
        /// </summary>
        public bool IsNativeTo(Species species) {
            return Arrays.IsEmpty(TargetSpecies)
                || Arrays.ContainsSafe(TargetSpecies, species.Id);
        }

        /// <param name="isVanilla">Tells if the part is meant for this particular species</param>
        public bool IsCompatibleWith(BodyPartSlot slot, out bool isVanilla) {
            isVanilla = false;
            if (slot == null) { return false; }
            if (slot.Type != SlotType) { return false; }
            var species = slot.Body.Owner.Species;
            var bodySize = species.Size;
            if (TargetSpecies != null) {
                isVanilla = Arrays.ContainsSafe(TargetSpecies, species.Id);
            }
            if (!isVanilla && CompatibleSizes != null) {
                if (!Arrays.Contains(CompatibleSizes, bodySize)) {
                    return false;
                }
            }
            if (!isVanilla && CompatibleStatFlags != StatFlags.None) {
                if ((species.StatFlags & CompatibleStatFlags) == 0) {
                    return false;
                }
            }
            switch (PartCompatibility) {
                case BodyPartCompatibility.Any: { return true; }
                case BodyPartCompatibility.TargetSpecies: {
                    return isVanilla;
                }
                case BodyPartCompatibility.Robotic: {
                    return !species.IsBiological;
                }
                case BodyPartCompatibility.Biological: {
                    // exception for Mindjar
                    if (HoldsMind && slot.Type.IdH == BodyPartSlotIdH.Brain) {
                        return true;
                    }
                    return species.IsBiological;
                }
                default: {
                    D.Err("Unhandled species compatibility type! {0}", Compatibility);
                    return false;
                }
            }
        }

        /// <summary>
        /// The slot size this part is worth when it has no BodyPartSlot to ask (e.g. a detached
        /// part sitting loose in the colony, being disposed of). A Crawler leg is a Leg slot,
        /// but a Crawler's Leg is a tenth of a human's, so falling back to SlotType.Size would
        /// overvalue it. Resolved lazily rather than cached at def-load time, since species
        /// aren't loaded yet when body part defs are.
        /// </summary>
        public float NativeSlotSize() {
            var spId = Arrays.SafeGet(TargetSpecies, 0);
            if (spId != null) {
                var sp = Species.Get(spId);
                if (sp?.BodyPartSlotSizes != null
                        && sp.BodyPartSlotSizes.TryGetValue(SlotType.Id, out var size)) {
                    return size;
                }
            }
            return SlotType.Size;
        }
        public override string ToString() {
            return $"BodyPartDef[{Id}]";
        }

        public static void FinalizeKnown() {
            foreach (var bp in All.Values) {
                if (!bp.Flags.HasFlag(BodyPartFlags.Biological)) {
                    if (bp.PartCompatibility == BodyPartCompatibility.TargetSpecies) {
                        foreach (var s in bp.TargetSpecies) {
                            var species = Species.Get(s);
                            if (species == null) {
                                D.Err("Incorrect target species in body part def: {0}: {1}", bp, s);
                                continue;
                            }
                            if (species.IsBiological) {
                                bp.Flags |= BodyPartFlags.Biological;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
