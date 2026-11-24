#if !PRODUCTION_BUILD
#define VERBOSE_STR
#endif
using System;
using Game.Constants;
using Game.Data;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Trade {
    [MessagePackObject]
    public struct MatAvailability {
        /// <summary>
        /// item1 = mat id h
        /// item2 == availability
        /// 0f - not available
        /// ~0.1f - very scarce
        /// 1f - default
        /// 2f - abundant
        /// </summary>
        [Key("Availabilities")] public (int, float)[] Availabilities;

        public float For(MatType matType) {
            return For(matType.IdH);
        }

        public float PriceAdjustmentFor(MatType matType) {
            var matIdH = matType.IdH;
            var avail = For(matIdH);
            if (avail == 1f) { return 1f; }
            if (avail > 1f) {
                var excess = avail - 1f;
                // if avail == 2x, we'll get 70% of the initial price
                switch (matType.MatRarity) {
                    case 0: return 1f - excess * 0.4f;
                    case 1: return 1f - excess * 0.3f;
                    case 2: return 1f - excess * 0.2f;
                    case 3: return 1f - excess * 0.1f;
                    default: return 1f - excess * 0.3f;
                }
            } else {
                switch (matType.MatRarity) {
                    case 0: return Mathf.Lerp(1.1f, 1.0f, avail);
                    case 1: return Mathf.Lerp(1.2f, 1.0f, avail);
                    case 2: return Mathf.Lerp(1.3f, 1.0f, avail);
                    case 3: return Mathf.Lerp(1.4f, 1.0f, avail);
                    default: return Mathf.Lerp(1.3f, 1.0f, avail);
                }
            }
        }
        public int AdjustTraderQuantityFor(MatType matType, int quantity) {
            if (quantity < 2) {
                return quantity;
            }
            var avail = For(matType.IdH);
            if (avail == 1f) { return quantity; }
            if (avail > 1f) {
                var excess = avail - 1f;
                // 150% stock
                switch (matType.MatRarity) {
                    case 0: return Mathf.CeilToInt(quantity * (1f + excess * 0.7f));
                    case 1: return Mathf.CeilToInt(quantity * (1f + excess * 0.5f));
                    case 2: return Mathf.CeilToInt(quantity * (1f + excess * 0.3f));
                    case 3: return Mathf.CeilToInt(quantity * (1f + excess * 0.2f));
                    default: return Mathf.CeilToInt(quantity * (1f + excess * 0.5f));
                }
            } else {
                // 50% stock
                switch (matType.MatRarity) {
                    case 0: return Mathf.CeilToInt(quantity * Mathf.Lerp(0.6f, 1f, avail));
                    case 1: return Mathf.CeilToInt(quantity * Mathf.Lerp(0.5f, 1f, avail));
                    case 2: return Mathf.CeilToInt(quantity * Mathf.Lerp(0.4f, 1f, avail));
                    case 3: return Mathf.CeilToInt(quantity * Mathf.Lerp(0.3f, 1f, avail));
                    default: return Mathf.CeilToInt(quantity * Mathf.Lerp(0.5f, 1f, avail));
                }
            }
        }

        public float For(int matIdH) {
            if (Availabilities == null) {
                return 1f;
            }
            for (int i = Availabilities.Length - 1; i >= 0; i--) {
                (int, float) a = Availabilities[i];
                if (a.Item1 == matIdH) {
                    return a.Item2;
                }
            }
            return 1f;
        }

        public override string ToString() {
            if (Availabilities == null) {
                return "MatAvailability[Blank]";
            }
            #if !VERBOSE_STR
            return $"MatAvailability[{D.Dump(Availabilities)}]";
            #else
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append("MatAvailability[");
            for (int i = Availabilities.Length - 1; i >= 0; i--) {
                (int, float) av = Availabilities[i];
                var mat = MatType.FastGet(av.Item1);
                if (mat == null) {
                    sb.AppendFormat("MISSING_MAT: {0}", av.Item1);
                } else {
                    sb.Append(mat.NameT);
                }
                sb.Append(':');
                sb.Append(av.Item2);
                sb.Append(';');
            }
            sb.Append(']');
            return sb.ToString();
            #endif
        }

    }
}
