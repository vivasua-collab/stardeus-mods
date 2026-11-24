#if UNITY_EDITOR
// #define VERBOSE_STATS
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Systems.Stats {
    public sealed class Stat {
        public enum ShowAs {
            Float,
            FloatTiny,
            Int,
            OnOff,
            YesNo,
            Percentage,
            PercentageSigned,
            Milliliters,
            RatePerSecond,
            RatePerMinute,
            RatePerHour,
            RatePerDay,
            PercentPerDay,
            PercentPerHour,
            /// <summary>
            /// Raw means that we don't multiply the value by ticks per hour
            /// We store the value as percentage, but display it as percent per hour
            /// </summary>
            PercentPerHourRaw,
            /// <summary>
            /// Raw means that we don't multiply the value by ticks per hour
            /// We store the value as percentage, but display it as percent per hour
            /// </summary>
            PercentPerDayRaw,
            PercentageWithFraction,
            Temperature,
            TemperatureDiff
        }
        private BFloat bFloat;
        private BInt bInt;
        public float Value { get; private set; }

        private bool hasBFloat;
        private bool hasBInt;
        public BFloat BFloat {
            get {
                if (!hasBFloat) {
                    bFloat = new BFloat(Value);
                    hasBFloat = true;
                }
                return bFloat;
            }
        }
        public BInt BInt {
            get {
                if (!hasBInt) {
                    bInt = new BInt(ValueInt);
                    hasBInt = true;
                }
                return bInt;
            }
        }

        [SkipDumpInstance]
        public float ValueFresh {
            get {
                Apply();
                return Value;
            }
        }
        #if VERBOSE_STATS
        private float _baseValue;
        public float BaseValue {
            get => D.GetFieldWithDebug(ref _baseValue, NameFor(this));
            set => D.SetFieldWithDebug(ref _baseValue, value, NameFor(this));
        }
        #else
        public float BaseValue;
        #endif
        public bool IsUnused;
        public bool IsDirty;
        public bool IsSourcesDirty;
        public readonly int Id;
        public readonly int CategoryId;
        public List<StatModifier> Modifiers;
        public List<StatDependency> InfluencedBy;
        public List<Stat> Influences;
        public List<IStatSource> Sources;
        public int ValueInt => (int) Value;
        public string IdStr;
        /// <summary>
        /// If this is not zero, the stat is only displayed if the being has a need with this ID
        /// </summary>
        public int RequiredNeedId;
        public int RequiredBodyPartId;
        public int RequiredAbilityId;
        private static readonly Dictionary<int, string> statNames = new();
        private static readonly Dictionary<int, string> statDescrs = new();
        private static readonly Dictionary<int, string> statCategories = new();
        private static readonly Dictionary<int, string> statIcons = new();
        public readonly ShowAs DisplayType;
        public float CompoundLimit;
        public float DefaultBase;
        public StatFlags Flags;
        /// <summary>
        /// Being species will have to have these stat flags (any of them)
        /// to get the stat
        /// </summary>
        public StatFlags FlagsRequired;
        /// <summary>
        /// Beings that have any of these flags will not use the stat. This has
        /// priority over FlagsRequiredAny
        /// </summary>
        public StatFlags FlagsForbidden;
        // Beings with any of these flags use the stat but do not show it: a robot's pain drives
        // its system shock, but "pain" is not something a player should read on a machine
        public StatFlags FlagsHidden;
        #if !PRODUCTION_BUILD
        public Action<Stat, float, float> OnBaseChange;
        public Action<Stat, float, float> OnValueChange;
        public object Owner;
        #endif

        public void Apply() {
            if (IsSourcesDirty) {
                RecalculateBase();
            }
            if (!IsDirty) { return; }
            var newVal = BaseValue;
            if (InfluencedBy != null) {
                float toAddPct = 0f;
                float toSubPct = 0f;
                float toAddFlat = 0f;
                for (int i1 = InfluencedBy.Count - 1; i1 >= 0; i1--) {
                    StatDependency inf = InfluencedBy[i1];
                    if (inf.IsAdditive) {
                        toAddFlat += inf.Addition;
                        continue;
                    }
                    var v = inf.Influence - 1f;
                    if (v > 0f) {
                        toAddPct = Mathf.Max(v, toAddPct);
                    } else {
                        toSubPct = Mathf.Min(v, toSubPct);
                    }
                }
                var toChangePct = toAddPct + toSubPct;
                if (toChangePct != 0f) {
                    newVal *= 1f + toChangePct;
                }
                // after the percentages: a stat that starts at 0 (a dodge chance) can only be
                // raised by adding to it
                newVal += toAddFlat;
            }
            if (!Arrays.IsEmpty(Modifiers)) {
                float toAddPct = 0f;
                float toSubPct = 0f;
                float toAdd = 0f;
                float toSet = 0f;
                float min = float.MaxValue;
                float max = float.MinValue;
                bool needsSet = false;
                bool needsMin = false;
                bool needsMax = false;
                for (int i1 = 0; i1 < Modifiers.Count; i1++) {
                    StatModifier mod = Modifiers[i1];
                    switch (mod.Type) {
                        case StatModifier.ModifierType.Adds: {
                            toAdd += mod.Value;
                            break;
                        }
                        case StatModifier.ModifierType.AddsPercent: {
                            var v = mod.Value;
                            if (v > 0f) {
                                toAddPct += v;
                                //toAddPct = Mathf.Max(v, toAddPct);
                            } else {
                                // toSubPct += v;
                                toSubPct = Mathf.Min(v, toSubPct);
                            }
                            break;
                        }
                        case StatModifier.ModifierType.Sets: {
                            needsSet = true;
                            toSet = mod.Value;
                            break;
                        }
                        case StatModifier.ModifierType.Min: {
                            needsMin = true;
                            min = math.min(mod.Value, min);
                            break;
                        }
                        case StatModifier.ModifierType.Max: {
                            needsMax = true;
                            max = math.max(mod.Value, max);
                            break;
                        }
                    }
                }
                if (needsSet) {
                    newVal = toSet;
                } else {
                    var toChangePct = toAddPct + toSubPct;
                    if (toChangePct != 0f) {
                        newVal *= 1f + toChangePct;
                    }
                    newVal += toAdd;
                }
                if (needsMin && newVal < min) {
                    newVal = min;
                }
                if (needsMax && newVal > max) {
                    newVal = max;
                }
            }
            if (newVal < 0f && DisplayType != ShowAs.PercentageSigned) {
                newVal = 0f;
            }
            SetEffectiveValue(newVal);
            IsDirty = false;
        }

        public static string NameFor(Stat stat) {
            if (!statNames.TryGetValue(stat.Id, out var name)) {
                name = $"stat.{stat.IdStr}".T();
                statNames[stat.Id] = name;
            }
            return name;
        }
        public static string DescFor(Stat stat) {
            if (!statDescrs.TryGetValue(stat.Id, out var desc)) {
                desc = $"stat.{stat.IdStr}.desc".TOrNull();
                statDescrs[stat.Id] = desc;
            }
            return desc;
        }
        public static string CategoryFor(Stat stat) {
            if (statCategories.TryGetValue(stat.CategoryId, out var catName)) {
                return catName;
            }
            D.Err("Category name unavailable for stat! {0}", stat);
            return T.StatsFullTitle;
        }
        public static string IconFor(Stat stat) {
            if (!statIcons.TryGetValue(stat.Id, out var icon)) {
                icon = IconId.CInfo;
                statIcons[stat.Id] = icon;
            }
            return icon;
        }

        public Stat(string id, string categoryId,
                ShowAs displayType = ShowAs.Float, string nameT = null,
                string icon = null) {
            IdStr = id;
            this.DisplayType = displayType;
            Id = Hashes.S(id);
            CategoryId = Hashes.S(categoryId);
            if (!statNames.ContainsKey(Id)) {
                nameT ??= $"stat.{IdStr}".T();
                statNames[Id] = nameT;
                statDescrs[Id] = $"stat.{IdStr}.desc".TOrNull();
                icon ??= IconId.CInfo;
                statIcons[Id] = icon;
            }
            if (!statCategories.ContainsKey(CategoryId)) {
                var catName = $"stat.category.{categoryId}".T();
                statCategories[CategoryId] = catName;
            }
        }

        public Stat MarkUnused() {
            IsUnused = true;
            return this;
        }
        public Stat MarkUsed() {
            IsUnused = false;
            return this;
        }

        public void CompoundBase(float addValue, float limit, float min = 0f,
                bool expectNonZero = true) {
            if (expectNonZero && addValue == 0f) {
                D.Err("Trying to compound stat base by 0! {0}. Limit: {1}",
                    this, limit);
            }
            CompoundLimit = Maths.Max(limit, CompoundLimit);
            var max = CompoundLimit;
            if (max == 0f) {
                max = float.MaxValue;
            }
            var newBase = Maths.Clamp(BaseValue + addValue, min, max);
            UpdateBase(newBase);
        }

        /// <summary>
        /// This should only be used if CompoundLimit is > 0f.
        /// </summary>
        /// <param name="toAdd"></param>
        /// <returns>excess, anything over compound limit</returns>
        public float AddToBaseWithCompoundLimit(float toAdd) {
            if (toAdd == 0f) {
                #if !PRODUCTION_BUILD
                // Can be a legit case due to modifiers and such
                D.Err("Trying to AddToBaseWithCompoundLimit by 0! {0}", this);
                #endif
                return 0f;
            }
            if (CompoundLimit == 0f) {
                #if !PRODUCTION_BUILD
                // I'm silencing this in production, because there can be a legit case -
                // someone missing intestines for example
                D.Err("Trying to AddToBaseWithCompoundLimit when limit is 0! {0}", this);
                #endif
                return 0f;
            }
            var newBase = BaseValue + toAdd;
            float excess;
            if (newBase > CompoundLimit) {
                excess = newBase - CompoundLimit;
                newBase = CompoundLimit;
            } else {
                excess = 0f;
            }
            UpdateBase(newBase);
            return excess;
        }

        public bool TryChange(float diff) {
            if (diff == 0f) {
                return false;
            }
            var baseBefore = BaseValue;
            var newBase = BaseValue + diff;
            if (newBase < 0f) {
                newBase = 0f;
            } else if (CompoundLimit > 0f) {
                if (newBase > CompoundLimit) {
                    newBase = CompoundLimit;
                }
            }
            UpdateBase(newBase);
            return baseBefore != BaseValue;
        }

        public bool TryIncrease(float toAdd) {
            if (toAdd == 0f) {
                #if !PRODUCTION_BUILD
                D.Err("Trying to increase stat by 0! {0}", this);
                #endif
                return false;
            }
            var newBase = BaseValue + toAdd;
            bool isFull = false;
            if (CompoundLimit > 0f) {
                if (newBase > CompoundLimit) {
                    newBase = CompoundLimit;
                    isFull = true;
                }
            }
            UpdateBase(newBase);
            return !isFull;
        }

        /// <summary>
        /// Only drains BaseValue
        /// </summary>
        /// <param name="amt">How much we want to drain</param>
        /// <param name="reducedBy">actually drained</param>
        /// <returns>true if BaseValue was > 0f</returns>
        public bool TryDrain(float amt, out float reducedBy) {
            if (amt == 0f) {
                #if !PRODUCTION_BUILD
                // can be OK if modifiers are involved that set some rate to 0,
                // for example toilet rate.
                // D.Err("Trying to drain stat by 0! {0}", this);
                #endif
                reducedBy = 0f;
                return BaseValue > 0f;
            } else if (amt < 0f) {
                D.Err("Trying to drain stat by negative amount! {0} | {1}", amt, this);
                reducedBy = 0f;
                return BaseValue > 0f;
            }
            reducedBy = Maths.Min(amt, BaseValue);
            UpdateBase(BaseValue - reducedBy);
            return reducedBy > 0f;
        }

        /// <summary>
        /// Only drains BaseValue
        /// </summary>
        /// <param name="amt">How much we want to drain</param>
        /// <returns>true if BaseValue was > 0f</returns>
        public bool TryDrain(float amt, bool ignoreZero = false) {
            if (amt == 0f) {
                #if !PRODUCTION_BUILD
                if (!ignoreZero) {
                    // can happen when organs are removed!
                    // D.ErrSoft("Trying to drain stat by 0! {0}", this);
                }
                #endif
                return BaseValue > 0f;
            } else if (amt < 0f) {
                D.Err("Trying to drain stat by negative amount! {0} | {1}", amt, this);
                return BaseValue > 0f;
            }
            var reducedBy = Maths.Min(amt, BaseValue);
            UpdateBase(BaseValue - reducedBy);
            return reducedBy > 0f;
        }

        // A derived stat with no sources of its own (WorkSpeed) starts from this instead of 0,
        // and RecalculateBase returns to it, or a sources refresh would zero the stat
        public Stat WithDefaultBase(float value) {
            DefaultBase = value;
            BaseValue = value;
            Value = value;
            return this;
        }

        public Stat WithRequiredNeed(int needIdH) {
            RequiredNeedId = needIdH;
            return this;
        }
        public Stat WithRequiredBodyPart(int partIdH) {
            RequiredBodyPartId = partIdH;
            return this;
        }
        public Stat WithRequiredAbility(int idH) {
            RequiredAbilityId = idH;
            return this;
        }

        public Stat WithFlags(StatFlags flags) {
            Flags = flags;
            return this;
        }

        public Stat WithRequiredFlags(StatFlags flags) {
            FlagsRequired = flags;
            return this;
        }
        public Stat WithForbiddenFlags(StatFlags flags) {
            FlagsForbidden = flags;
            return this;
        }
        public Stat WithHiddenFor(StatFlags flags) {
            FlagsHidden = flags;
            return this;
        }
        public bool IsHiddenFor(StatFlags speciesFlags) {
            return (FlagsHidden & speciesFlags) != StatFlags.None;
        }

        public void UpdateBase(float value) {
            if (BaseValue == value) {
                return;
            }
            #if !PRODUCTION_BUILD
            OnBaseChange?.Invoke(this, BaseValue, value);
            #endif
            BaseValue = value;
            if (Modifiers == null && InfluencedBy == null) {
                SetEffectiveValue(value);
            } else {
                // we'll calculate new value from modifiers later
                IsDirty = true;
            }
        }

        public void SetEffectiveValue(float val) {
            if (Value == val) { return; }
            #if !PRODUCTION_BUILD
            OnValueChange?.Invoke(this, Value, val);
            #endif
            Value = val;
            if (hasBFloat) { bFloat.Set(val); }
            if (hasBInt) { bInt.Set(ValueInt); }
            if (Influences != null) {
                for (int i = Influences.Count - 1; i >= 0; i--) {
                    Stat inf = Influences[i];
                    inf.IsDirty = true;
                }
            }
        }

        public bool TryAddModifier(StatModifier mod, bool applyImmediately = false) {
            if (Modifiers?.Contains(mod) == true) {
                return false;
            }
            AddModifier(mod, applyImmediately);
            return true;
        }

        public void AddModifier(StatModifier mod, bool applyImmediately = false) {
            if (mod == null) {
                D.Err("Trying to add null modifier to stat {0}", this);
                return;
            }
            if (Modifiers == null) {
                Modifiers = new List<StatModifier>();
            }
            if (Modifiers.Contains(mod)) {
                D.Err("Trying to add same modifier to stat more than once! {0} / {1}", Id, mod);
                return;
            }
            Modifiers.Add(mod);
            IsDirty = true;
            if (applyImmediately) {
                Apply();
            }
        }

        public void RemoveModifier(StatModifier mod, bool warnIfMissing = true) {
            if (Modifiers == null) {
                return;
            }
            if (Modifiers.Remove(mod)) {
                IsDirty = true;
                Apply();
            } else {
                if (warnIfMissing) {
                    D.Err("Failed to remove a StatModifier from Stat: {0} -> {1})",
                        mod, this);
                }
            }
        }
        public string DisplayBase() {
            return Display(BaseValue);
        }
        public string Display() {
            return Display(Value);
        }
        public string DisplaySigned(float v) {
            if (v > 0f) {
                var val = Display(v);
                if (val.StartsWith('-') || val.StartsWith('+')) {
                    return val;
                }
                return $"+{val}";
            } else if (v < 0f) {
                var val = Display(-v);
                if (val.StartsWith('-') || val.StartsWith('+')) {
                    return val;
                }
                return $"-{val}";
            } else {
                return Display(0f);
            }
        }

        public string Display(float v, bool colorPercentage = false) {
            switch (DisplayType) {
                case ShowAs.Float: return v.ToString("0.##");
                case ShowAs.FloatTiny: return v.ToString("0.####");
                case ShowAs.Int: return Units.Num((int) v);
                case ShowAs.OnOff: return v == 0f ? T.Off : T.On;
                case ShowAs.YesNo: return v == 0f ? T.No : T.Yes;
                case ShowAs.Milliliters: return string.Format("{0:0}{1}", v * 1000f, T.UnitShortMilliliter);
                case ShowAs.RatePerHour: return string.Format("{0:0.###}/{1}", v * Consts.TicksPerHour, T.UnitShortHour);
                case ShowAs.RatePerSecond: return string.Format("{0:0}/{1}", v * Consts.TicksPerSecond, T.UnitShortSecond);
                case ShowAs.RatePerMinute: return string.Format("{0:0.###}/{1}", v * Consts.TicksPerMinute, T.UnitShortMinute);
                case ShowAs.RatePerDay: return string.Format("{0:0.###}/{1}", v * Consts.TicksPerDay, T.UnitShortDay);
                case ShowAs.PercentPerHour: return string.Format("{0}/{1}", Units.PercentageWithFraction2(v * Consts.TicksPerHour), T.UnitShortHour);
                case ShowAs.PercentPerDay: return string.Format("{0}/{1}", Units.PercentageWithFraction2(v * Consts.TicksPerDay), T.UnitShortDay);
                case ShowAs.PercentPerHourRaw: return string.Format("{0}/{1}", Units.PercentageWithFraction2(v), T.UnitShortHour);
                case ShowAs.PercentPerDayRaw: return string.Format("{0}/{1}", Units.PercentageWithFraction2(v), T.UnitShortDay);
                case ShowAs.Percentage: {
                    if (colorPercentage) {
                        return Units.PercentageColored(v, clamp: false);
                    }
                    return Units.Percentage(v, clamp: false);
                }
                case ShowAs.PercentageSigned: return Units.PercentageSigned(v);
                case ShowAs.PercentageWithFraction: return Units.PercentageWithFraction2(v, clamp: false);
                case ShowAs.Temperature: return Texts.Temperature(v);
                case ShowAs.TemperatureDiff: return Texts.TemperatureDiff(v);
            }
            return v.ToString();
        }

        public override string ToString() {
            #if PRODUCTION_BUILD
            if (Modifiers == null) {
                return $"Stat[{IdStr}:{Display()}]";
            } else {
                return $"Stat[{IdStr}:{BaseValue}->{D.Dump(Modifiers)}->{Display()}]";
            }
            #else
            if (Modifiers == null) {
                return $"Stat[{IdStr}:{Display()}:Owner:{Owner}]";
            } else {
                return $"Stat[{IdStr}:{BaseValue}->{D.Dump(Modifiers)}->{Display()}:Owner:{Owner}]";
            }
            #endif
        }

        public void SourcesChanged() {
            #if VERBOSE_STATS
            D.Warn("Sources changed for stat {0}:", Id);
            #endif
            IsSourcesDirty = true;
        }

        public void RecalculateBase() {
            IsSourcesDirty = false;
            UpdateBase(DefaultBase);
            CompoundLimit = 0f;
            if (Arrays.IsEmpty(Sources)) {
                // this can happen if we damage / remove a body part, it's ok
                // D.Err("Sources changed for stat that has no sources! {0}", this);
                return;
            }
            for (int i = Sources.Count - 1; i >= 0; i--) {
                IStatSource s = Sources[i];
                s.UpdateStat(this);
            }
        }

        public string StatString() {
            return $"{NameFor(this)}: <b>{Display()}</b>";
        }

        public void LoadValue(float value) {
            Value = value;
        }
    }
}
