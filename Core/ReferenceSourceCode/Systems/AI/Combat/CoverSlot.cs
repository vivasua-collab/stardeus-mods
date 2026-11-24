using System;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using System.Text;
using Game.Constants;
using KL.Utils;
using Game.Input;

namespace Game.Systems.AI.Combat {
    public struct CoverSlot {
        public int PosIdx;
        // 100 = full cover
        // 0 = no cover
        public short Total;
        public byte N;
        public byte E;
        public byte S;
        public byte W;
        public byte NE;
        public byte SE;
        public byte NW;
        public byte SW;

        public byte InDir(Dir dir) {
            switch (dir) {
                case Dir.N: return N;
                case Dir.E: return E;
                case Dir.W: return W;
                case Dir.S: return S;
                case Dir.NE: return NE;
                case Dir.SE: return SE;
                case Dir.SW: return SW;
                case Dir.NW: return NW;
                default: return 0;
            }
        }

        public void UpdateTotal() {
            Total = (short) (N + E + W + S + NE + SE + NW + SW);
        }

        /// <summary>
        /// The strongest directional cover value across all 8 directions.
        /// More reliable than Total for ranking "any cover" queries because
        /// Total double-counts shared neighbor contributions.
        /// </summary>
        public byte MaxDir {
            get {
                byte max = Maths.Max(N, S);
                max = Maths.Max(max, E);
                max = Maths.Max(max, W);
                max = Maths.Max(max, NE);
                max = Maths.Max(max, SE);
                max = Maths.Max(max, NW);
                max = Maths.Max(max, SW);
                return max;
            }
        }

        public override string ToString() {
            return $"Cover[{Pos.String(PosIdx)}:{Total}]";
        }

        public void AppendDescription(StringBuilder sb, bool subItem, int pos) {
            string text;
            if (Total == 0) {
                text = T.CombatCoverAtNone_V1.F(Texts.Accented2(Pos.String(pos)));
                text = Texts.Red(text);
                if (subItem) {
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight, text);
                } else {
                    sb.Append(text);
                }
                return;
            }
            #if !PRODUCTION_BUILD
            if (pos != PosIdx) {
                // can happen during the attack, when the worker jumps out of pos
                D.ErrSoft("Pos of cover does not match! expected {0}, actual {1}",
                    Pos.String(pos),
                    Pos.String(PosIdx));
                pos = PosIdx;
            }
            #endif
            text = T.CombatCoverAt_V1.F(Texts.Accented2(Pos.String(pos)));
            AppendDetails(sb, text, subItem);
        }

        public void AppendDetails(StringBuilder sb, string prefix, bool subItem) {
            if (subItem) {
                sb.AppendFormat("\n {0} {1}: ", GlyphMap.ArrowRight, prefix);
            } else {
                sb.Append(prefix);
                sb.Append(": ");
            }
            if (Total == 0) {
                sb.Append(T.NotAvailable);
                return;
            }
            bool needsComma = false;
            needsComma = AppendCoverInDirection(sb, N, needsComma, T.DirectionNorthShort);
            needsComma = AppendCoverInDirection(sb, NE, needsComma, T.DirectionNorthEastShort);
            needsComma = AppendCoverInDirection(sb, E, needsComma, T.DirectionEastShort);
            needsComma = AppendCoverInDirection(sb, SE, needsComma, T.DirectionSouthEastShort);
            needsComma = AppendCoverInDirection(sb, S, needsComma, T.DirectionSouthShort);
            needsComma = AppendCoverInDirection(sb, SW, needsComma, T.DirectionSouthWestShort);
            needsComma = AppendCoverInDirection(sb, W, needsComma, T.DirectionWestShort);
            AppendCoverInDirection(sb, NW, needsComma, T.DirectionNorthWestShort);
        }

        private bool AppendCoverInDirection(StringBuilder sb, byte cover,
                bool needsComma, string dirName) {
            if (cover == 0) {
                return needsComma;
            }
            if (needsComma) {
                sb.Append(", ");
            }
            sb.Append(Texts.Accented(dirName));
            sb.Append(':');
            if (cover < 33) {
                sb.Append(Texts.Red(cover.ToString()));
            } else if (cover > 66) {
                sb.Append(Texts.Green(cover.ToString()));
            } else {
                sb.Append(Texts.Yellow(cover.ToString()));
            }
            return true;
        }
    }
}
