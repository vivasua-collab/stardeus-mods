using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Diplomacy;
using Game.Utils;

namespace Game.Systems.UIUX {
    // An empty consequence list reads to the player as a broken or missing tooltip, so the
    // absence of consequences is always spelled out instead of hiding the tooltip or section.
    public static class GiftConsequenceTip {
        public static string ForPackage(GiftPackage package) {
            bool canReject = package.Direction != GiftDirection.NPCToPlayer;
            bool hasAccept = HasAny(package.AcceptConsequences);
            bool hasReject = canReject && HasAny(package.RejectConsequences);
            // A package that is always accepted has no reject branch to describe, so spelling
            // out that it is free would be noise, not reassurance.
            bool showReject = hasReject || (canReject && package.AcceptChance < 1f);
            if (!hasAccept && !showReject) {
                return NoneLine();
            }

            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(Texts.SubTitle(T.NounConsequences));
            AppendSection(sb,
                canReject ? T.ConsequenceAccept : T.ConsequenceTake,
                package.AcceptConsequences, package.AcceptChance, true, canReject);
            if (showReject) {
                sb.AppendLine();
                AppendSection(sb, T.ConsequenceReject, package.RejectConsequences,
                    1f - package.AcceptChance, false, true);
            }
            return sb.ToString();
        }

        public static string ForCancel(GiftPackage package) {
            if (!HasAny(package.RejectConsequences)) {
                return NoneLine();
            }

            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(Texts.SubTitle(T.NounConsequences));
            AppendRows(sb, package.RejectConsequences);
            return sb.ToString();
        }

        private static bool HasAny(GiftPackage.Consequence[] consequences) {
            return consequences != null && consequences.Length > 0;
        }

        private static string NoneLine() {
            return Texts.SubTitle($"{T.NounConsequences}: {T.RiskNone}");
        }

        private static void AppendSection(StringBuilder sb, string title,
                GiftPackage.Consequence[] consequences,
                float chance, bool isGood, bool showChance) {
            title = Texts.Accented(title);
            if (showChance) {
                if (isGood) {
                    var accChance = Units.PercentageColored(chance, 0.85f, 0.5f);
                    title = $"{title} {Texts.GreyParentheses(accChance)}";
                } else {
                    var rejChance = Units.PercentageColoredInverse(chance, 0.15f, 0.5f);
                    title = $"{title} {Texts.GreyParentheses(rejChance)}";
                }
            }

            sb.AppendLine(title);
            if (!HasAny(consequences)) {
                sb.AppendFormat(" {0} {1}\n", Texts.Accented(GlyphMap.ArrowRight), T.RiskNone);
                return;
            }
            AppendRows(sb, consequences);
        }

        private static void AppendRows(StringBuilder sb,
                GiftPackage.Consequence[] consequences) {
            foreach (var con in consequences) {
                sb.AppendFormat(" {0} {1}\n", Texts.Accented(GlyphMap.ArrowRight), con.Text);
            }
        }
    }
}
