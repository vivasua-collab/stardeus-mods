using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using System.Text;
using Game.Input;
using Game.Data.Factions;

namespace Game.Systems.Items {
    [Serializable]
    public sealed class InventoryItemDef : IAbilitySourceItem {
        public static readonly Dictionary<int, InventoryItemDef> All = new();
        private static readonly Dictionary<int, InventoryItemDef> byDef = new();
        // should match actual object id
        public int IdH;
        public InventorySlotType SlotType;
        public InventoryLooksFlags ShowFlags;
        public InventoryLooksFlags HideFlags;
        public ulong HidesMask;
        public Def Def;
        public string ItemName => Def.NameT;
        public string Id;
        public string DefId;
        public string SlotId;
        public string HidesSlotId;
        public string SpriteId;
        public string ColorGroupId;
        public float Rarity;
        public float CombatStrengthBonus;
        /// <summary>
        /// CombatStrengthBonus plus the best AttackMethods DPS, i.e. the same quantity
        /// CombatRaid.StrengthOf sums for equipped items. Derived at load, not authored -
        /// used to bias gear generation toward stronger items for higher raid gear tiers
        /// (see GenItemFlags.WellArmed/EliteArmed) without a separate authored strength value.
        /// </summary>
        public float CombatValue;
        // The best attack method's DPS as authored
        public float MaxDPS;
        // Ranged reach is worth some damage: a gun hits from where the enemy can't, so a ranged
        // method weighs up to RangeWeightMax more at RangeWeightFullTiles (WeightedDps). The
        // distance grew with the 1.5x weapon ranges, so weapon scores and prices stayed put.
        private const float RangeWeightMax = 0.3f;
        private const float RangeWeightFullTiles = 22.5f;
        /// <summary>
        /// If this is defined with Check: true in item's JSON file,
        /// the situation check will run before spawning this item in raider's
        /// inventory, or friendly equipped unit's inventory
        /// </summary>
        public Situation SpawnRequirements;
        [JsonEnum(typeof(Game.Systems.Items.InventoryLooksFlags))]
        public string[] LooksShowFlags;
        [JsonEnum(typeof(Game.Systems.Items.InventoryLooksFlags))]
        public string[] LooksHideFlags;
        public string[] HidesLookParts;
        public string[] AddsAbilities;
        public string[] AddsConditions;
        public StatSourceData[] StatSources;
        /// <summary>
        /// item1 is stat id hash
        /// </summary>
        public (int, StatModifier)[] StatModifiers;
        public AttackMethodData[] AttackMethods;
        public ComponentConfig[] ProvidesStats;
        public ComponentConfig[] AddsStatModifiers;
        // Apply only while the item shows on the wearer (InventoryItemSlot.IsVisibleNow):
        // armor slows its wearer once it is out, not under the clothes indoors
        public ComponentConfig[] AddsStatModifiersWhenShown;
        public (int, StatModifier)[] StatModifiersWhenShown;
        public ComponentConfig[] AddsAttackMethods;
        public int RequiredJobId;
        // Species flags the wearer's body (not its mind) must have, e.g. Humanoid for
        // weapons a robot frame cannot wield
        [JsonEnum(typeof(Game.Systems.Stats.StatFlags))]
        public string[] RequiresBodyFlags;
        public StatFlags RequiredBodyFlags;
        /// <summary>
        /// Derived from AttackMethods at load: Ranged if any method shoots, Melee if
        /// the item attacks at all, Any otherwise (armour, clothing, hats).
        /// </summary>
        public WeaponKind Kind;

        [ExecuteOnReload]
        private static void CleanupStatics() {
            // We want them to be fresh in unity
            All.Clear();
            byDef.Clear();
            qualityDps = null;
            qualityAccuracy = null;
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }

        private static void LinkSlots() {
            foreach (var k in All) {
                var item = k.Value;
                item.SlotType = InventorySlotType.Get(item.SlotId);
                if (item.SlotType == null) {
                    D.Err("Failed loading item slot id {0} for item {1}", item.SlotId, item.Id);
                }
            }
        }

        private static void LoadKnown() {
            if (All.Count > 0) { return; }

            var items = The.ModLoader.LoadObjects<InventoryItemDef>("Config/Inventory/Items");
            foreach (var item in items) {
                item.IdH = Hashes.S(item.Id);
                var def = The.Defs.Get(item.DefId);
                item.Def = def;
                item.StatSources = StatSourceData.Deserialize(item.ProvidesStats, def);
                item.StatModifiers = StatModifier.Deserialize(item.AddsStatModifiers, def.NameT, def);
                item.StatModifiersWhenShown = StatModifier.Deserialize(
                    item.AddsStatModifiersWhenShown, def.NameT, def);
                item.AttackMethods = AttackMethodData.Deserialize(item.AddsAttackMethods);
                if (!Arrays.IsEmpty(item.AttackMethods)) {
                    item.RequiredJobId = JobTypeIdH.Melee;
                    item.Kind = WeaponKind.Melee;
                    foreach (var am in item.AttackMethods) {
                        if (am.AttackAction == "Ranged") {
                            item.RequiredJobId = JobTypeIdH.Shooting;
                            item.Kind = WeaponKind.Ranged;
                            break;
                        }
                    }
                }
                if (!Arrays.IsEmpty(item.RequiresBodyFlags)) {
                    item.RequiredBodyFlags = Enums.FlagsFromStringArray_UInt<StatFlags>(
                        item.RequiresBodyFlags, item);
                }
                item.CombatValue = item.CombatStrengthBonus;
                if (!Arrays.IsEmpty(item.AttackMethods)) {
                    var maxDps = 0f;
                    var maxWeighted = 0f;
                    foreach (var am in item.AttackMethods) {
                        if (am.DPS > maxDps) { maxDps = am.DPS; }
                        var weighted = WeightedDps(am, 1f, 1f);
                        if (weighted > maxWeighted) { maxWeighted = weighted; }
                    }
                    item.MaxDPS = maxDps;
                    item.CombatValue += maxWeighted;
                }
                if (item.Rarity == 0f) {
                    var msg = string.Format(
                        "InventoryItem has no Rarity value, setting to 1.0: {0}", item.Id);
                    D.Err(msg);
                    ModRegistry.WarnAboutBrokenMod(def, msg);
                    item.Rarity = 1f;
                }
                if (!Arrays.IsEmpty(item.LooksShowFlags)) {
                    foreach (var f in item.LooksShowFlags) {
                        var fen = Enums.FromString<InventoryLooksFlags>(f);
                        item.ShowFlags |= fen;
                    }
                }
                if (!Arrays.IsEmpty(item.LooksHideFlags)) {
                    foreach (var f in item.LooksHideFlags) {
                        var fen = Enums.FromString<InventoryLooksFlags>(f);
                        item.HideFlags |= fen;
                    }
                }
                if (!Arrays.IsEmpty(item.HidesLookParts)) {
                    foreach (var f in item.HidesLookParts) {
                        var type = LookPartTypeDef.Get(f);
                        if (type != null) {
                            item.HidesMask |= type.Bit;
                        }
                    }
                }
                // TODO set all properties and shit
                if (All.TryAdd(item.IdH, item)) {
                    if (!byDef.TryAdd(def.IdH, item)) {
                        D.Err("InventoryItem by def already added for def: {0}. Existing: {1}. New: {2}",
                            def, byDef[def.IdH], item.Id);
                    }
                } else {
                    D.Err("InventoryItem already added by id: {0}", item.Id);
                }
            }
            Ready.When(WhenH.CoreLoaded2, LinkSlots);
        }

        private static Curve qualityDps;
        private static Curve qualityAccuracy;

        /// <summary>
        /// How good this weapon is, objectively, for a being that can use it: the best attack
        /// method's damage per second times its peak accuracy, both at this quality, ranged
        /// methods weighted up by reach (RangeWeight), plus CombatStrengthBonus (a shield's
        /// armor, a baton's stuns). Auto equip picks weapons by this, not by market value,
        /// which fluctuates and says nothing about which of two weapons fights better.
        /// </summary>
        public float WeaponScore(float quality) {
            if (Arrays.IsEmpty(AttackMethods)) { return CombatStrengthBonus; }
            float best = 0f;
            foreach (var am in AttackMethods) {
                var score = ScoreOf(am, quality);
                if (score > best) { best = score; }
            }
            return CombatStrengthBonus + best;
        }

        // One attack's WeightedDps as authored, at the quality of what carries it
        private static float ScoreOf(AttackMethodData am, float quality) {
            QualityMuls(quality, out var dpsMul, out var accMul);
            return WeightedDps(am, dpsMul, accMul);
        }

        // How the quality of what carries an attack (an item, a body part) scales its DPS and
        // peak accuracy
        public static void QualityMuls(float quality, out float dpsMul, out float accMul) {
            qualityDps ??= CurveDef.FastGet(CurveIdH.Attacks_QualityDPS);
            qualityAccuracy ??= CurveDef.FastGet(CurveIdH.Attacks_QualityMaxAccuracy);
            dpsMul = qualityDps.Evaluate(quality);
            accMul = qualityAccuracy.Evaluate(quality);
        }

        private static float WeightedDps(AttackMethodData am, float dpsMul, float accMul) {
            var accuracy = am.AccuracyMax > 0f ? am.AccuracyMax : 1f;
            return WeightedDps(am.DPS * dpsMul, accuracy * accMul, am.AttackAction == "Ranged",
                am.RangeMax);
        }

        /// <summary>
        /// What an attack is worth in a fight: damage per second times peak accuracy, ranged
        /// attacks weighted up by reach. The one measure weapon choice (WeaponScore), raider
        /// gear weighting (CombatValue) and raid pricing (CombatRaid.StrengthOf) share, so the
        /// weapon a being prefers is also the one it is priced by.
        /// </summary>
        public static float WeightedDps(float dps, float accuracy, bool isRanged, float rangeMax) {
            var weight = isRanged
                ? 1f + RangeWeightMax * Mathf.Clamp01(rangeMax / RangeWeightFullTiles)
                : 1f;
            return dps * accuracy * weight;
        }

        public static InventoryItemDef FastGet(int id) {
            if (All.TryGetValue(id, out var item)) {
                return item;
            }
            return null;
        }
        public static InventoryItemDef Get(string id) {
            return Get(id, Hashes.S(id));
        }

        public static bool HasForDef(int defIdH) {
            return byDef.ContainsKey(defIdH);
        }

        public static bool TryForDef(int defIdH, out InventoryItemDef item) {
            return byDef.TryGetValue(defIdH, out item);
        }

        public static InventoryItemDef Get(string id, int idH) {
            if (All.TryGetValue(idH, out var item)) {
                return item;
            }
            D.Err("InventoryItem {0} ({1}) not found! See Config/Inventory/Items folder",
                id, idH);
            return null;
        }

        public override string ToString() {
            return $"InventoryItem [{Id}]";
        }

        public bool IsCompatibleWith(BeingData being) {
            if (being == null) {
                D.Err("Trying to check inventory item compatibility with null being data");
                return true;
            }
            if (!IsAllowedFor(being.Species, being.EffectiveFaction)) {
                return false;
            }
            if (RequiredJobId != 0 && !being.Mind.Jobs.CanDo(RequiredJobId)) {
                return false;
            }
            return true;
        }

        public bool FitsBody(Species body) {
            if (RequiredBodyFlags == StatFlags.None) { return true; }
            if (body == null) { return false; }
            return (body.StatFlags & RequiredBodyFlags) == RequiredBodyFlags;
        }

        /// <summary>
        /// A weapon the law keeps from robot frames (RobotWeaponLaw): Humanoid is its only
        /// body requirement. Any other body requirement is a physical misfit.
        /// </summary>
        public bool IsIllegalFor(Species body) {
            if (body == null || RequiredBodyFlags != StatFlags.Humanoid) { return false; }
            return !FitsBody(body);
        }

        public bool IsAllowedFor(Species body, FactionDef faction) {
            if (FitsBody(body)) { return true; }
            return IsIllegalFor(body) && RobotWeaponLaw.Exempts(faction);
        }

        /// <summary>
        /// Why IsCompatibleWith refuses this being, or null when it does not.
        /// </summary>
        public string IncompatibilityReason(BeingData being) {
            if (!IsAllowedFor(being.Species, being.EffectiveFaction)) {
                return IsIllegalFor(being.Species) ? T.AdjIllegal : T.AdjIncompatible;
            }
            if (RequiredJobId != 0 && !being.Mind.Jobs.CanDo(RequiredJobId)) {
                var jobName = RequiredJobId == JobTypeIdH.Shooting
                    ? T.JobTypeShooting : T.JobTypeMelee;
                return $"{jobName}: {T.AdjIncapable}";
            }
            return null;
        }

        // Only Humanoid is used today: robots are kept from these by law, and the player's
        // Asimov Override lifts it, after which there is nothing to say
        private string BodyRequirementInfo() {
            if (RequiredBodyFlags == StatFlags.None) { return null; }
            if (RequiredBodyFlags != StatFlags.Humanoid) {
                return Texts.Red(T.AdjIncompatible);
            }
            if (RobotWeaponLaw.Exempts(FactionDef.Player)) { return null; }
            return $"{Texts.Red(T.AdjIllegal)}\n{Texts.Grey(T.WeaponIllegalForRobots)}";
        }
        public void AddInfo(StringBuilder sb, float quality, bool shortStats, bool addBRHR = true) {
            bool needsBR = false;
            if (AttackMethods != null) {
                needsBR = true;
                if (addBRHR) {
                    sb.Append(GlyphMap.BRHR);
                }
                var subTitle = T.NounAttackMethods;
                var at100pct = Texts.GreyParentheses(
                    T.AtQuality_V1.F(Units.Percentage(quality, clamp: false)));
                sb.Append(Texts.SubTitle($"{subTitle} {at100pct}"));
                int num = 0;
                foreach (var am in AttackMethods) {
                    AttackMethodData.AppendInfo(sb, quality, shortStats, am, num++);
                }
            }
            if (AddsAbilities != null)  {
                Caches.StringList.Clear();
                foreach (var ab in AddsAbilities) {
                    var ability = Ability.Get(ab);
                    if (ability != null) {
                        Caches.StringList.Add(Texts.Accented(ability.NameT, false));
                    }
                }
                if (Caches.StringList.Count > 0) {
                    needsBR = true;
                    if (addBRHR) {
                        sb.Append(GlyphMap.BRHR);
                    }
                    sb.Append(Texts.SubTitle(T.ProvidesAbilities));
                    Caches.StringList.Sort();
                    sb.AppendJoin(", ", Caches.StringList);
                }
            }
            StatsBeing statsBeing = null;
            if (StatSources != null)  {
                Caches.StringList.Clear();
                statsBeing ??= new StatsBeing();
                foreach (var ss in StatSources) {
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

                    found.UpdateBase(ss.Amount * Consts.QualityStatCurve.Evaluate(quality));
                    Caches.StringList.Add(found.StatString());
                }
                if (Caches.StringList.Count > 0) {
                    if (addBRHR) {
                        sb.Append(GlyphMap.BRHR);
                    } else if (needsBR) {
                        sb.AppendLine();
                    }
                    needsBR = true;
                    var atQ = T.AtQuality_V1.F(Units.Percentage(quality, clamp: false));
                    atQ = Texts.GreyParenthesesSmaller(atQ);
                    sb.Append(Texts.SubTitle($"{T.ProvidesStats} {atQ}"));
                    Caches.StringList.Sort();
                    bool needsNewLine = false;
                    foreach (var stat in Caches.StringList) {
                        if (needsNewLine) {
                            sb.AppendLine();
                        } else {
                            needsNewLine = true;
                        }
                        sb.AppendFormat("{0} {1}", GlyphMap.ArrowRight, stat);
                    }
                }
            }
            AppendStatModifiers(sb, StatModifiers, T.ModifiesStats, quality, addBRHR,
                ref needsBR, ref statsBeing);
            // only while the item shows (armor out of a safe room, drafted, in combat)
            AppendStatModifiers(sb, StatModifiersWhenShown,
                $"{T.ModifiesStats} ({T.NounCombat})", quality, addBRHR, ref needsBR,
                ref statsBeing);
            var bodyRequirement = BodyRequirementInfo();
            if (bodyRequirement != null) {
                if (addBRHR) {
                    sb.Append(GlyphMap.BRHR);
                } else if (needsBR) {
                    sb.AppendLine();
                }
                sb.Append(bodyRequirement);
            }
        }

        private void AppendStatModifiers(StringBuilder sb, (int, StatModifier)[] modifiers,
                string title, float quality, bool addBRHR, ref bool needsBR,
                ref StatsBeing statsBeing) {
            if (modifiers == null) { return; }
            statsBeing ??= new StatsBeing();
            bool addedSMHeader = false;
            foreach (var sm in modifiers) {
                var statId = sm.Item1;
                var smod = sm.Item2.QualityAdjustedCopy(quality);
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

                bool needsNewLine = true;
                if (!addedSMHeader) {
                    addedSMHeader = true;
                    if (addBRHR) {
                        sb.Append(GlyphMap.BRHR);
                    } else if (needsBR) {
                        sb.AppendLine();
                    }
                    var atQ = T.AtQuality_V1.F(Units.Percentage(quality, clamp: false));
                    sb.Append(Texts.SubTitle($"{title} {Texts.GreyParentheses(atQ)}"));
                    needsNewLine = false;
                    needsBR = true;
                }

                found.UpdateBase(0);
                // sb.AppendFormat("\n - {0}", Stat.NameFor(found));
                smod.AppendInfo(sb, found, needsNewLine: needsNewLine, null,
                    subItem: true, subItemGrey: false);
            }
        }

        public bool GetStatBaseFor(Stat stat, float quality,
                out float toAdd, out float limit) {
            for (int i = StatSources.Length - 1; i >= 0; i--) {
                StatSourceData src = StatSources[i];
                if (src.StatId == stat.Id) {
                    var stateMul = src.StateMul;
                    if (stateMul > 0f)  {
                        if (quality > 0f) {
                            stateMul *= Consts.QualityStatCurve.Evaluate(quality);
                            toAdd = src.Amount * stateMul;
                        } else {
                            toAdd = 0f;
                        }
                    } else {
                        toAdd = src.Amount;
                    }
                    limit = src.CompoundLimit;
                    return true;
                }
            }
            D.Err("Could not get stat base from inventory item {0} for stat {1}", Id, stat);
            toAdd = 0f;
            limit = 0f;
            return false;
        }

        public (int, StatModifier)[] QualityAdjustedStatModifiers(float quality) {
            return QualityAdjusted(StatModifiers, quality);
        }

        public (int, StatModifier)[] QualityAdjustedStatModifiersWhenShown(float quality) {
            return QualityAdjusted(StatModifiersWhenShown, quality);
        }

        private static (int, StatModifier)[] QualityAdjusted((int, StatModifier)[] modifiers,
                float quality) {
            if (Arrays.IsEmpty(modifiers)) {
                return null;
            }
            var len = modifiers.Length;
            var res = new (int, StatModifier)[len];
            for (int i = 0; i < len; i++) {
                (int, StatModifier) sm = modifiers[i];
                res[i] = (sm.Item1, sm.Item2.QualityAdjustedCopy(quality));
            }
            return res;
        }
    }
}
