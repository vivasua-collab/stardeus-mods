#if !PRODUCTION_BUILD
// #define DEBUG_OPS
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Creatures.Surgeries {
    public sealed class SurgicalOperation {
        private int? id;
        public int Id => id ??= Hashes.Quick((int) TargetBeing.Id, TargetSlot?.IdH ?? 0, (int) OpType);
        public BeingData TargetBeing;
        public BodyPartSlot TargetSlot;
        public SurgicalOpType OpType;
        public ObjData ReservedPart;
        public readonly B<SurgicalOpState> State = new B<SurgicalOpState>(SurgicalOpState.Undefined);
        public bool IsActive => (State.Value & SurgicalOpState.Active) == State.Value;
        public string Icon;
        public string Desc;
        public string FailReason;
        public AIGoal AssociatedGoal;
        public long CreatedAt;
        /// <summary>
        /// When non-null, only these Mind IDs may operate the SurgeryHub for this procedure.
        /// </summary>
        public uint[] RestrictedOperatorMindIds;

        public string Tooltip(bool accent) {
            GetOpTexts(out var opName, out var name, out var side, out var tip);
            string q = side == null ? null : $"{T.NounQuality}: {side}";
            var slotName = TargetSlot?.NameT ?? TargetBeing.Body.BodySlot.NameT;
            if (accent) {
                slotName = Texts.Accented(slotName);
            }
            if (q == null) {
                return $"{slotName}: {name}";
            }
            return $"{slotName}: {name} {Texts.GreyParentheses(q)}";
        }

        public void UpdateDesc() {
            if (Icon == null) {
                switch (OpType) {
                    case SurgicalOpType.Install: {
                        Icon = IconId.CBodyInstallOne;
                        break;
                    }
                    case SurgicalOpType.Remove: {
                        Icon = IconId.CBodyRemoveOne;
                        break;
                    }
                    case SurgicalOpType.RemoveAll: {
                        Icon = IconId.CBodyRemoveAll;
                        break;
                    }
                    case SurgicalOpType.Swap: {
                        Icon = IconId.CBodyReplaceBad;
                        break;
                    }
                    default: {
                        D.Err("Unhandled OpType: {0}", OpType);
                        Icon = IconId.CBodyReplaceBad;
                        break;
                    }
                }
            }
            if (TargetBeing.Species.IsBiological) {
                Desc = T.PartPerformSurgery;
            } else {
                Desc = T.PartPerformReplacement;
            }
        }

        public bool Validate(bool verbose) {
            if (OpType == SurgicalOpType.Undefined) {
                if (verbose) {
                    D.Err("OpType is undefined! {0}", this);
                    return false;
                }
            }
            if (TargetBeing == null) {
                if (verbose) {
                    D.Err("Slot has no TargetBeing! {0}", this);
                }
                return false;
            }
            if (TargetSlot == null) {
                if ((OpType & SurgicalOpType.MassOperation) != OpType) {
                    if (verbose) {
                        D.Err("Slot has no TargetSlot but is not a mass operation! {0}", this);
                    }
                    return false;
                }
            } else if (TargetSlot.Body.Owner != TargetBeing) {
                D.Err("Trying to register surgical op where being and slot are different! {0}", this);
                return false;
            }
            if (ReservedPart == null) {
                if (OpType == SurgicalOpType.Install || OpType == SurgicalOpType.Swap) {
                    if (verbose) {
                        D.Err("Install or swap op has no reserved part! {0}", this);
                    }
                    return false;
                }
            }
            if (OpType == SurgicalOpType.Remove) {
                if (!TargetSlot.CanRemovePart(verbose)) {
                    return false;
                }
            }
            if (OpType == SurgicalOpType.Install || OpType == SurgicalOpType.Swap) {
                var deadEnd = TargetSlot.BeyondRepairAncestor;
                if (deadEnd != null) {
                    FailReason = T.BodyPartUnusableViaParent_V1.F(deadEnd.NameT);
                    return false;
                }
            }
            if (OpType == SurgicalOpType.ReplaceIrreparableAndMissing
                    || OpType == SurgicalOpType.FillMissing) {
                if (!TargetBeing.Body.BodySlot.IsUsableWithParent) {
                    D.Err("Perhaps allow it?");
                    FailReason = $"{T.CondDamaged}: {TargetBeing.Body.BodySlot.NameT}";
                    return false;
                }
            }
            return true;
        }

        public static string NameFor(SurgicalOpType type, bool forProgressBar) {
            switch (type) {
                case SurgicalOpType.Remove: return T.BodyOpRemove;
                case SurgicalOpType.Install: return T.BodyOpInstall;
                case SurgicalOpType.Swap: return T.BodyOpSwap;
                case SurgicalOpType.RemoveAll: return T.BodyOpRemoveAll;
                case SurgicalOpType.RemoveIrreparable: return T.BodyOpRemoveIrreparable;
                case SurgicalOpType.FillMissing: return T.BodyOpInstallMissing;
                case SurgicalOpType.ReplaceIrreparableAndMissing: return T.BodyOpReplaceIrreparable;
                default: {
                    D.Err("Unhandled name for OperationType: {0}", type);
                    return type.ToString();
                }
            }
        }

        public static string BodyPartName(BodyPartDef def, BodyPartState state) {
            string name = def.NameT;
            string glyph = null;
            if (state.IsInfected) {
                name = Texts.Yellow(name);
                glyph = GlyphMap.SpritePoison;
            } else if (!state.IsUsableSelf) {
                if (state.IsDestroyed) {
                    name = Texts.Red(Texts.Strike(name));
                    glyph = GlyphMap.SpriteCrutch;
                } else {
                    name = Texts.Yellow(Texts.Strike(name));
                    glyph = GlyphMap.SpriteBlood;
                }
            } else {
                if (!state.IsUsableParent) {
                    name = Texts.Strike(name);
                }
                if (Arrays.Count(state.Wounds) > 0) {
                    name = Texts.Yellow(name);
                    glyph = GlyphMap.SpriteBlood;
                }
            }
            if (glyph == null) {
                return name;
            }
            return $"{glyph} {name}";
        }

        internal void GetOpTexts(out string opName, out string name, out string side, out string tip) {
            opName = NameFor(OpType, false);
            name = null;
            side = null;
            tip = null;
            if (ReservedPart != null) {
                var sb = Caches.StringBuilder4;
                sb.Clear();
                sb.Append(TargetSlot.IsPartRemoved ? T.Empty : BodyPartName(TargetSlot.InstalledDef, TargetSlot.State));
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.Append(BodyPartName(ReservedPart.BodyPart, ReservedPart.BodyPartState));
                name = sb.ToString();
                sb.Clear();
                sb.Append(Units.PercentageColored(TargetSlot.Quality, ReservedPart.Quality, ReservedPart.Quality, clamp: false));
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.Append(Units.PercentageColored(ReservedPart.Quality, TargetSlot.Quality, TargetSlot.Quality, clamp: false));
                side = sb.ToString();
                sb.Clear();
                sb.Append(Texts.SubTitle(opName));
                sb.AppendLine(name);
                sb.Append(T.NounQuality);
                sb.Append(": ");
                sb.AppendLine(side);
                sb.AppendLine();
                sb.Append(Texts.SubTitle(T.NounSuccessChance));
                if (!TargetSlot.IsPartRemoved) {
                    float extractChance = TargetSlot.EstimateChanceOfSuccess(null, TargetSlot.State, true, true);
                    sb.Append(T.Remove);
                    sb.Append(": ");
                    sb.Append(TargetSlot.InstalledDef.NameT);
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(" ~");
                    sb.AppendLine(Units.PercentageColored(extractChance));
                }
                float installChance = TargetSlot.EstimateChanceOfSuccess(null, ReservedPart.BodyPartState, false, true);
                sb.Append(T.Install);
                sb.Append(": ");
                sb.Append(ReservedPart.BodyPart.NameT);
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(" ~");
                sb.AppendLine(Units.PercentageColored(installChance));
                sb.Append(Texts.Accented($"{T.Warning}!"));
                sb.Append(' ');
                sb.AppendLine(T.BodyPartReplacementWarning);
                tip = sb.ToString();
            } else {
                name = opName;
                if (TargetSlot != null) {
                    var sb = Caches.StringBuilder4;
                    sb.Clear();
                    sb.Append(Texts.SubTitle(T.NounSuccessChance));
                    float extractChance = TargetSlot.EstimateChanceOfSuccess(null, TargetSlot.State, true, true);
                    sb.Append(T.Remove);
                    sb.Append(": ");
                    sb.Append(TargetSlot.InstalledDef.NameT);
                    sb.Append(' ');
                    sb.Append(GlyphMap.ArrowRight);
                    sb.Append(" ~");
                    sb.AppendLine(Units.PercentageColored(extractChance));
                    SurgeryUtils.AppendAlsoRemoved(sb, TargetSlot);
                    sb.Append(Texts.Accented($"{T.Warning}!"));
                    sb.Append(' ');
                    sb.AppendLine(T.BodyPartReplacementWarning);
                    tip = sb.ToString();
                }
            }
        }
        public static string GetMassOpTooltip(Body body, SurgicalOpType opType) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            var slots = body.Slots;
            for (int i = 0; i < slots.Length; i++) {
                var slot = slots[i];
                bool matches = false;
                // below a slot that can never work again a part only comes out (STA-1749)
                var deadEnd = slot.BeyondRepairAncestor != null;
                switch (opType) {
                    case SurgicalOpType.RemoveAll:
                        matches = !slot.IsPartRemoved && slot.CanRemovePart(false);
                        break;
                    case SurgicalOpType.RemoveIrreparable:
                        matches = !slot.IsPartRemoved && slot.CanRemovePart(false)
                            && (slot.State.IsDestroyed || deadEnd);
                        break;
                    case SurgicalOpType.FillMissing:
                        matches = slot.IsPartRemoved && !slot.IsDestroyed && !slot.Type.IsOptional
                            && !deadEnd;
                        break;
                    case SurgicalOpType.ReplaceIrreparableAndMissing:
                        matches = (slot.IsPartRemoved && !slot.IsDestroyed && !slot.Type.IsOptional
                                && !deadEnd)
                            || (!slot.IsPartRemoved && slot.CanRemovePart(false)
                                && (slot.State.IsDestroyed || deadEnd));
                        break;
                }
                if (matches) {
                    sb.AppendFormat("{0}: {1}\n", slot.Type.NameT,
                        Texts.Accented(slot.InstalledDef?.NameT)
                            ?? Texts.Red(T.Missing));
                }
            }
            return sb.ToString();
        }

        public void PostRemove() {
            if (ReservedPart?.Reservation != null) {
                ReservedPart.TryUnreserveForOperation(this);
            }
            if (AIGoal.IsActive(AssociatedGoal) || AIGoal.IsAvailable(AssociatedGoal)) {
                AIGoal.Cancel(AssociatedGoal, G.Ticks, T.AdRejCancelled);
            }
        }

        public override string ToString() {
            return $"SurgicalOp [{OpType}|{State}|{TargetSlot?.Id}|{TargetBeing.Being}]";
        }

    }
}
