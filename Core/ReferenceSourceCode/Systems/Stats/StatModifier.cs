using System;
using System.Text;
using Game.Components;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Input;

namespace Game.Systems.Stats {
    public sealed class StatModifier {
        public enum ModifierType {
            Adds,
            /// <summary>
            /// If two modifiers add different negative percentage, it won't accumulate,
            /// instead the smallest negative value will be used.
            /// This is to prevent things like -80% sleep rate on top of -40% sleep
            /// rate would produce negative or zero sleep rate
            /// </summary>
            AddsPercent,
            Sets,
            Min,
            Max,
        }
        [System.NonSerialized] public float Value;
        /// <summary>
        /// -200% would be -2f
        /// +50% would be +0.5f
        /// </summary>
        public ModifierType Type;
        public string NameT;
        public StatModifier QualityAdjustedCopy(float quality) {
            var adj = Consts.QualityStatCurve.Evaluate(quality);
            var copy = new StatModifier {
                Value = Value * adj,
                Type = Type,
                NameT = NameT,
            };
            return copy;
        }
        public static StatModifier Add(float adds, string nameT) {
            return new StatModifier {
                Value = adds,
                NameT = nameT,
                Type = ModifierType.Adds,
            };
        }
        public static StatModifier Max(float max, string nameT) {
            return new StatModifier {
                Value = max,
                NameT = nameT,
                Type = ModifierType.Max,
            };
        }
        public static StatModifier AddPercent(float addsPercent, string nameT) {
            return new StatModifier {
                Value = addsPercent,
                NameT = nameT,
                Type = ModifierType.AddsPercent,
            };
        }

        public static StatModifier Sets(float value, string nameT) {
            return new StatModifier {
                Value = value,
                NameT = nameT,
                Type = ModifierType.Sets,
            };

        }

        public static (int, StatModifier)[] Deserialize(ComponentConfig[] data, string name, object ctx) {
            if (data == null) { return null; }
            var res = new (int, StatModifier)[data.Length];
            for (int i = data.Length - 1; i >= 0; i--) {
                ComponentConfig d = data[i];
                float value = d.GetFloat(PropertyIdH.Value, 0f);
                var type = Enums.FromString<ModifierType>(d.GetString(PropertyIdH.Type, "Adds"));
                var mod = new StatModifier() {
                    NameT = name,
                    Value = value,
                    Type = type ,
                };
                var item = (d.ComponentH, mod);
                res[i] = item;
            }
            return res;
        }
        public override string ToString() {
            switch (Type) {
                case ModifierType.Adds: {
                    if (Value > 0f) {
                        return $"Mod[{NameT}|+{Value}]";
                    } else if (Value < 0f) {
                        return $"Mod[{NameT}|{Value}]";
                    } else {
                        return $"Mod[{NameT}|+0]";
                    }
                }
                case ModifierType.AddsPercent: {
                    return $"Mod[{NameT}|{Units.PercentageSigned(Value)}]";
                }
                case ModifierType.Sets: {
                    return $"Mod[{NameT}|={Value}]";
                }
                case ModifierType.Min: {
                    return $"Mod[{NameT}|>={Value}]";
                }
                case ModifierType.Max: {
                    return $"Mod[{NameT}|<={Value}]";
                }
                default: {
                    return $"Mod[{NameT}|Invalid!]";
                }
            }
        }

        public bool AppendInfo(StringBuilder sb, Stat forStat, bool needsNewLine,
                string nameOverride = null, bool subItem = false, bool subItemGrey = true) {
            if (needsNewLine) {
                sb.Append('\n');
            }
            if (subItem) {
                if (subItemGrey) {
                    sb.AppendFormat(" {0} ", Texts.Grey(GlyphMap.ArrowRight));
                    sb.Append(Texts.Grey(nameOverride ?? Stat.NameFor(forStat)));
                    sb.Append(Texts.Grey(":"));
                } else {
                    sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
                    sb.Append(nameOverride ?? Stat.NameFor(forStat));
                    sb.Append(':');
                }
            } else {
                sb.Append(nameOverride ?? Stat.NameFor(forStat));
                sb.Append(':');
            }
            sb.Append(' ');
            var showAs = forStat.DisplayType;
            string color;
            // open the right color
            if (subItem && subItemGrey) {
                color = Texts.TMPColorGrey;
            } else if ((forStat.Flags & StatFlags.IsNegative) == StatFlags.IsNegative) {
                if (Value > 0) {
                    color = Texts.TMPColorRed;
                } else {
                    color = Texts.TMPColorGreen;
                }
            } else {
                if (Value > 0) {
                    color = Texts.TMPColorGreen;
                } else {
                    color = Texts.TMPColorRed;
                }
            }
            sb.AppendFormat("<color={0}>", color);
            sb.Append(DisplayValue(forStat));
            sb.Append("</color>");
            return true;
        }

        public string DisplayValue(Stat forStat) {
            switch (Type) {
                case ModifierType.Adds: {
                    if (forStat.DisplayType == Stat.ShowAs.Temperature) {
                        return Texts.TemperatureDiff(Value);
                    }
                    var disp = forStat.Display(Value);
                    if (Value > 0f && !disp.StartsWith('+')) {
                        return $"+{disp}";
                    }
                    return disp;
                }
                case ModifierType.AddsPercent: {
                    return Units.PercentageSigned(Value);
                }
                case ModifierType.Sets: {
                    return $"={DisplayAbsolute(forStat)}";
                }
                case ModifierType.Min: {
                    return $">={DisplayAbsolute(forStat)}";
                }
                case ModifierType.Max: {
                    return $"<={DisplayAbsolute(forStat)}";
                }
                default: {
                    return forStat.Display(Value);
                }
            }
        }

        // Sets/Min/Max carry an absolute value, a forced sign would be misleading
        private string DisplayAbsolute(Stat forStat) {
            switch (forStat.DisplayType) {
                case Stat.ShowAs.Temperature: return Texts.Temperature(Value);
                case Stat.ShowAs.PercentageSigned: return Units.Percentage(Value, clamp: false);
                default: return forStat.Display(Value);
            }
        }
    }
}
