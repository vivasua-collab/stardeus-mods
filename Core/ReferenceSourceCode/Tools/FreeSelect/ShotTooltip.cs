using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Stats;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Tools {
    // What a shot of the selected shooter at the hovered enemy looks like: the hit chance from
    // the formula the attack rolls against, or why it can't hit from where it stands. Without
    // it an attack order on a target out of reach looked accepted and then nothing happened.
    public static class ShotTooltip {
        public static void AppendHeader(StringBuilder sb, CombatUnit target) {
            sb.Append(Texts.SubTitle($"{T.NounAttackTarget}: {target.Data.Name}"));
        }

        // Starts its own tooltip block when there is no block about the target to continue
        // (outside hostile combat).
        public static void Append(FreeSelectContext ctx, CombatUnit shooter, CombatUnit target,
                bool newBlock) {
            var attack = shooter.PreviewBestAttack();
            if (attack == null || attack.IsMelee) { return; }
            var sb = ctx.Tooltip;
            // a subtitle ends its own line, so the first line after it needs no newline
            bool atLineStart = false;
            if (newBlock) {
                sb = ctx.BeginTooltipBlock();
                AppendHeader(sb, target);
                atLineStart = true;
            }
            var S = ctx.S;
            var dist = Vectors.Distance(shooter.Position, target.Position);
            var drafted = shooter.Agent.DirectControl.IsUnderDirectControl;
            if (dist > attack.Stats.RangeMax.Value) {
                AppendCannotHit(sb, ref atLineStart, T.CombatShotOutOfRange, true, drafted);
                return;
            }
            if (dist < attack.Stats.RangeMin.Value) {
                AppendCannotHit(sb, ref atLineStart, T.WarningTooClose, false, drafted);
                return;
            }
            // the shooter's own sight flag is measured for its current target only
            if (!EntityUtils.HasLineOfSightCover(shooter.SightPosition, target.SightPosition,
                    0.5f, CombatUnit.SightRange, out _)) {
                AppendCannotHit(sb, ref atLineStart, T.CombatShotNoSight, true, drafted);
                return;
            }
            attack.IsWithinAttackRange(dist, out var accPercent, out _);
            var odds = attack.HitChance(shooter, target.Position, target.PosIdx,
                target.Agent.Motion.VelocityPercent, accPercent, target.Data);
            NewLine(sb, ref atLineStart).AppendFormat(" {0} {1}: {2}", GlyphMap.ArrowRight,
                T.CombatShotHitChance, Texts.Accented(Units.Percentage(odds.Total)));
            // One number for the shooter: weapon at this distance times the shooter's Shooting
            // Accuracy stat (skill, eyes, hands) at this distance. More lines of percentages read
            // as noise, and the stat's own tooltip already explains its part. What the target's
            // situation takes away follows below.
            NewLine(sb, ref atLineStart).AppendFormat("    {0}: {1}", T.StatAccuracy,
                Units.Percentage(odds.Aim));
            if (odds.Dodge < 1f) {
                NewLine(sb, ref atLineStart).AppendFormat("    {0}: {1}",
                    Stat.NameFor(target.Data.StatsBeing.MeleeDodge),
                    Units.PercentageSigned(odds.Dodge - 1f));
            }
            if (odds.Cover < 1f) {
                NewLine(sb, ref atLineStart).AppendFormat("    {0}: {1}", T.OverlayTipCover,
                    Units.Percentage(odds.Cover));
            }
            if (odds.Leader > 1f) {
                NewLine(sb, ref atLineStart).AppendFormat("    {0}: {1}", T.NounLeader,
                    Units.Percentage(odds.Leader));
            }
            if (odds.Moving < 1f) {
                NewLine(sb, ref atLineStart).AppendFormat("    {0}: {1}",
                    T.CombatShotTargetMoving, Units.Percentage(odds.Moving));
            }
        }

        // A drafted shooter's order on such a target is refused unless Shift walks it closer
        // (CombatOrders); an undrafted unit's order always walks it closer
        private static void AppendCannotHit(StringBuilder sb, ref bool atLineStart, string reason,
                bool canApproach, bool drafted) {
            NewLine(sb, ref atLineStart).AppendFormat(" {0} {1}", GlyphMap.ArrowRight,
                Texts.Red(reason));
            if (!canApproach) { return; }
            var hint = drafted
                ? T.CombatShotShiftApproach_V2.F(
                    The.Bindings.GetControlName(ActionType.ShiftModifier),
                    The.Bindings.GetControlName(ActionType.ClickCancel))
                : T.CombatShotWillApproach;
            NewLine(sb, ref atLineStart).AppendFormat(" {0} {1}", GlyphMap.ArrowRight, hint);
        }

        private static StringBuilder NewLine(StringBuilder sb, ref bool atLineStart) {
            if (!atLineStart) {
                sb.Append('\n');
            }
            atLineStart = false;
            return sb;
        }
    }
}
