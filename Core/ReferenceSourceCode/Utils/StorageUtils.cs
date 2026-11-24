using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using KL.Utils;

namespace Game.Utils {
    public static class StorageUtils {
        private static List<string> filterModeOptions;
        public static List<string> FilterModeOptions {
            get {
                if (filterModeOptions == null) {
                    filterModeOptions = new List<string>();
                    foreach (StorageFilterMode mode in System.Enum.GetValues(typeof(StorageFilterMode))) {
                        filterModeOptions.Add(FormatStorageFilterMode(mode));
                    }
                }
                return filterModeOptions;
            }
        }
        public static string FormatStorageFilterMode(StorageFilterMode mode) {
            switch (mode) {
                case StorageFilterMode.AllowAll: return T.FilterModeAllowall;
                case StorageFilterMode.ForbidAll: return T.FilterModeForbidall;
                case StorageFilterMode.AllowSelected: return T.FilterModeAllowselected;
                case StorageFilterMode.ForbidSelected: return T.FilterModeForbidselected;
                default: {
                    D.Err("Unhandled StorageFilterMode: {0}", mode);
                    return T.Unknown;
                }
            }
        }
        public static string QuantityChoiceLabel(int qty, int num) {
            var label = Units.Num(num);
            if (num == qty) {
                label = $"{label} {Texts.GreyParentheses(T.All)}";
            }
            if (num > 0 && num == qty / 2) {
                label = $"{label} {Texts.GreyParentheses(T.TexHalf)}";
            }
            if (num > 4 && num == qty / 4) {
                label = $"{label} {Texts.GreyParentheses(T.TexQuarter)}";
            }

            return label;
        }

        public static IOrderedEnumerable<int> QuantityChoiceOptions(int qty,
            bool includeZero = true,
            bool includeRoundNumbers = true) {
            var ints = Caches.IntSet;
            ints.Clear();
            if (includeZero) {
                ints.Add(0);
            }
            ints.Add(1);
            if (includeRoundNumbers) {
                if (qty > 10) { ints.Add(10); }
                if (qty > 50) { ints.Add(50); }
                if (qty > 100) { ints.Add(100); }
                if (qty > 250) { ints.Add(250); }
                if (qty > 500) { ints.Add(500); }
            }
            if (qty > 1) {
                ints.Add(qty / 2);
            }
            if (qty > 4) {
                ints.Add(qty / 4);
            }
            ints.Add(qty);
            return ints.OrderBy(i => i);
        }

    }
}
