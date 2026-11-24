using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Creatures.Surgeries {
    public static class SurgeryUtils {
        // Surgery on someone who is not on our side needs them down (SurgeryChoiceView refuses
        // it otherwise). A raider who comes to on the table or in the queue no longer qualifies:
        // left there, he would be a hostile nobody can reach and the fight would never end.
        public static bool IsAwakeHostilePatient(BeingData data) {
            return data.HasStateFlag(BeingState.Alive) && !data.IsFriendly
                && !data.Body.IsIncapacitated;
        }

        /// <summary>
        /// Applies a durable Sharp wound for a surgical failure outcome, since Condition and the
        /// derived state flags (Destroyed, Incapacitated) are rebuilt from Wounds by
        /// BodyPartState.UpdateCondition on every call - writing them directly gets silently
        /// reverted the next time condition is recalculated (e.g. by decay/rot ticking).
        /// </summary>
        private static void ApplyFailureWound(BodyPartState state, BodyPartDef part,
                BodyPartSlot slot, Severity sev, string source) {
            if (part.IsIndestructible && sev >= Severity.Irreparable) {
                sev = Severity.Critical;
            } else if (sev == Severity.Irreparable && slot.State == state
                    && slot.IsRequiredForBeingAlive) {
                // same normalization TryAddWound/TryAddOrUpdateWound apply to installed parts
                sev = Severity.Fatal;
            }
            var w = state.WoundFor(DamageType.Sharp);
            if (w.Severity < sev) {
                w.Severity = sev;
                w.HealAmt = 0f;
            }
            if (source != null) {
                w.Source = source;
            }
            if (w.CreatedAt == 0) {
                w.CreatedAt = G.Ticks;
            }
            if (part.IsBiological) {
                Wound.UpdateScarSeverity(ref w);
            }
            state.UpdateWound(w);
            state.UpdateCondition(state.IsUsableParent, part.SlotType);
        }

        /// <summary>
        /// Rolls exactly one failure outcome (severity-priority order) and applies its effect.
        /// </summary>
        /// <returns>The single failure type that was applied.</returns>
        public static SurgicalOpFailureType ApplySurgicalOpFailure(Rng rng,
                BeingData surgeon, BodyPartDef part, BodyPartState state, BodyPartSlot slot, bool isExtract,
                SurgicalOpType opType = SurgicalOpType.Undefined) {
            return ApplySurgicalOpFailure(rng, surgeon, part, state, slot, isExtract, opType, null);
        }

        // performerLink names who performed the operation when there is no surgeon (an
        // automated hub). With neither a surgeon nor a performer nothing is reported, which
        // keeps disposal and mass body part removal quiet.
        public static SurgicalOpFailureType ApplySurgicalOpFailure(Rng rng,
                BeingData surgeon, BodyPartDef part, BodyPartState state, BodyPartSlot slot, bool isExtract,
                SurgicalOpType opType, string performerLink) {

            bool wasPartDestroyed = state.IsDestroyed;
            int skill = 0;
            if (surgeon != null) {
                var skillId = slot.Body.Owner.Species.IsBiological ? JobTypeIdH.Medical : JobTypeIdH.Robotics;
                if (surgeon.Mind.Jobs.TryJob(skillId, out var job)) {
                    skill = job.Level;
                }
            }

            bool isBiological = slot.Body.Owner.Species.IsBiological;
            Curve curve;

            // Roll candidates worst-to-least-severe; first hit wins.
            var failType = SurgicalOpFailureType.None;

            // fatality?
            if (slot.Body.Owner.HasStateFlag(BeingState.Alive) && rng.Chance(part.SlotType.FatalityRisk)) {
                curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_Fatality);
                if (rng.Chance(curve.Evaluate(skill))) {
                    failType = SurgicalOpFailureType.Fatality;
                }
            }

            // destroy part?
            if (failType == SurgicalOpFailureType.None) {
                curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_DestroyPart);
                if (rng.Chance(curve.Evaluate(skill))) {
                    failType = SurgicalOpFailureType.DestroyPart;
                }
            }

            // destroy slot? (extract operations only)
            if (failType == SurgicalOpFailureType.None && isExtract) {
                curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_DestroySlot);
                if (rng.Chance(curve.Evaluate(skill))) {
                    failType = SurgicalOpFailureType.DestroySlot;
                }
            }

            // infect body? (biological only)
            if (failType == SurgicalOpFailureType.None && isBiological) {
                curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_InfectBody);
                if (rng.Chance(curve.Evaluate(skill))) {
                    failType = SurgicalOpFailureType.InfectBody;
                }
            }

            // cripple part?
            if (failType == SurgicalOpFailureType.None) {
                curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_CripplePart);
                if (rng.Chance(curve.Evaluate(skill))) {
                    failType = SurgicalOpFailureType.CripplePart;
                }
            }

            // weld part? (not hierarchies, not wrecks)
            if (failType == SurgicalOpFailureType.None) {
                if (slot.HasAttachedSlots || (slot.AttachedToPart != null && slot.AttachedToPart.Type.IsRemovable)) {
                    // never weld hierarchies, only single parts that are attached to body or head
                } else if (isExtract && wasPartDestroyed) {
                    // a destroyed part welded in would cost the slot for good (STA-1804)
                } else {
                    curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_WeldPart);
                    if (rng.Chance(curve.Evaluate(skill))) {
                        failType = SurgicalOpFailureType.WeldPart;
                    }
                }
            }

            // delayed rejection? (biological implants only)
            if (failType == SurgicalOpFailureType.None && isBiological && !isExtract) {
                var rejectionRisk = part.SlotType.RejectionRisk;
                if (!part.IsNativeTo(slot.Body.Owner.Species)) {
                    // xenografts (paws on a human, human hands on a cat) are rejected more often.
                    // Organs name no species at all - they are graded by body size - so they are
                    // native to whoever they fit and must not land here.
                    rejectionRisk *= Tunables.BodyPartForeignRejectionRiskMul;
                }
                if (rng.Chance(rejectionRisk)) {
                    failType = SurgicalOpFailureType.Rejection;
                }
            }

            // fallback: wound the part
            if (failType == SurgicalOpFailureType.None) {
                failType = SurgicalOpFailureType.WoundPart;
            }

            // InfectBody is not yet implemented — fall back to wound so a real effect is applied
            if (failType == SurgicalOpFailureType.InfectBody) {
                D.Warn("TODO: infect body with something");
                failType = SurgicalOpFailureType.WoundPart;
            }

            var woundSource = surgeon != null
                ? $"{surgeon.Mind.LinkToShortName} ({T.NounFailedSurgery})"
                : T.NounFailedSurgery;

            // Apply the single chosen failure
            switch (failType) {
                case SurgicalOpFailureType.Fatality: {
                    var name = T.NounSurgery;
                    if (surgeon != null) {
                        name = $"{name} ({Texts.LinkToMind(surgeon.Mind)})";
                    }
                    BeingUtils.Kill(A.S, slot.Body.Owner, isBiological ? DamageType.Organ : DamageType.Energy,
                        NamedEntity.For(name), T.NounSurgery, (bps) => bps.Type.IdH == BodyPartSlotIdH.Body || bps == slot);
                    break;
                }
                case SurgicalOpFailureType.DestroyPart:
                    ApplyFailureWound(state, part, slot, Severity.Irreparable, woundSource);
                    break;
                case SurgicalOpFailureType.DestroySlot:
                    slot.IsDestroyed = true;
                    break;
                case SurgicalOpFailureType.CripplePart:
                    state.Flags |= BodyPartStateFlags.Crippled;
                    ApplyFailureWound(state, part, slot, Severity.Critical, woundSource);
                    break;
                case SurgicalOpFailureType.WeldPart:
                    state.Flags |= BodyPartStateFlags.Welded;
                    break;
                case SurgicalOpFailureType.Rejection:
                    // nothing to do, part is ejected
                    break;
                case SurgicalOpFailureType.WoundPart: {
                    // wound severity is random, from mild to fatal
                    curve = CurveDef.FastGet(CurveIdH.Surgery_FailChance_Skill_WoundMaxSeverity);
                    var maxSev = (int) curve.Evaluate(skill);
                    var sev = (Severity) rng.Range((int) Severity.Mild, maxSev + 1);
                    ApplyFailureWound(state, part, slot, sev, woundSource);
                    break;
                }
            }

            var performer = surgeon != null ? surgeon.Mind.LinkToShortName : performerLink;
            if (performer != null && !IsSwappedOutPartDamage(failType, isExtract, opType, slot)) {
                var desc = DescribeFailure(failType, part);
                var msg = $"{T.SurgicalOpFailed_V3.F(performer, slot.Body.Owner.Mind.LinkToShortName, slot.NameT)}. {desc}";

                // The Event Log is the record of every failure, so the player can find out
                // what happened without watching. Only the fanfare is gated, to avoid spam.
                A.S.Sys.Log.AddLine(slot.Body.Owner, msg);
                if (surgeon != null) {
                    A.S.Sys.Log.AddLine(surgeon, msg);
                }

                bool isHarmlessDestroyedPartFailure = isExtract && wasPartDestroyed
                    && (failType == SurgicalOpFailureType.WoundPart
                        || failType == SurgicalOpFailureType.CripplePart
                        || failType == SurgicalOpFailureType.DestroyPart);

                // taking apart a dead body (e.g. an automated hub removing everything from a
                // frame whose core just broke): a fanfare per slot failure would be noise
                bool isDeadBodyDismantling = !slot.HoldsMind
                    && slot.Body.Owner.HasStateFlag(BeingState.Dead);

                if (!isHarmlessDestroyedPartFailure && !isDeadBodyDismantling) {
                    // Mind-holding slots (Brain / Core) are critical: a welded or destroyed one
                    // traps the mind permanently, so notify for every meaningful consequence -
                    // even during RemoveAll, where per-slot failures are otherwise silenced to
                    // avoid spam, and even for non-player patients.
                    // Automated hubs run whole production batches unattended, so without a
                    // surgeon only single-slot procedures raise a fanfare.
                    bool allowFanfare = surgeon != null
                        ? opType != SurgicalOpType.RemoveAll
                        : IsSingleSlotOp(opType);
                    if (slot.HoldsMind || (slot.Body.Owner.IsPlayer && allowFanfare)) {
                        A.S.Sig.ShowFanfare.Send(msg);
                    }
                }
            }

            var log = string.Format("Surgical operation on {0} ({1}) failed. Outcome: {2}. Performed by {3} with skill {4}",
                slot.NameT, part.NameT, failType, surgeon?.Name ?? T.ScanTargetNone, skill);
            D.Warn("{0} -> {1}", slot.Body.Owner.Mind.Name, log);

            return failType;
        }

        // A part can't stay attached to nothing: once a procedure leaves a slot empty (a plain
        // removal, or a swap whose implant didn't take), everything hanging off it comes out too,
        // or a colonist ends up with a foot and no leg. The player ordered one operation, so the
        // attached parts come off without failure rolls of their own.
        public static void DetachOrphanedParts(GameState s, BodyPartSlot slot, List<Obj> spawned) {
            if (!slot.IsPartRemoved) { return; }
            var attached = slot.AttachedParts;
            if (Arrays.IsEmpty(attached)) { return; }
            for (int i = attached.Length - 1; i >= 0; i--) {
                var child = attached[i];
                if (!child.IsPartRemoved
                        && child.TryRemovePart(s, out var obj, null, false, out _, false)) {
                    if (obj != null) {
                        spawned.Add(obj);
                    }
                    CancelOrderOnDetachedPart(s, child);
                }
                // also runs for an already empty child, whose own parts may have been
                // orphaned before removals cascaded
                DetachOrphanedParts(s, child, spawned);
            }
        }

        // An order queued for a part that just came off with its parent has nothing left to work
        // on: a Remove is already done and a Swap has no part to replace. Left in the queue it
        // would either fail loudly on the empty slot or never be ordered at all, blocking every
        // later surgery for the patient.
        private static void CancelOrderOnDetachedPart(GameState s, BodyPartSlot slot) {
            var op = s.Query.FindSurgicalOperation.Ask(slot, false);
            if (op == null || op.State.Value == SurgicalOpState.Ongoing) { return; }
            if (op.OpType != SurgicalOpType.Remove && op.OpType != SurgicalOpType.Swap) { return; }
            op.State.Set(SurgicalOpState.Canceled);
            s.Query.RegisterSurgicalOperation.Ask(op, false);
        }

        public static void AppendAlsoRemoved(StringBuilder sb, BodyPartSlot slot) {
            int start = sb.Length;
            AppendInstalledDescendants(sb, slot, start);
            if (sb.Length == start) { return; }
            var names = sb.ToString(start, sb.Length - start);
            sb.Length = start;
            sb.AppendLine(T.SurgicalOpAlsoRemoves_V1.F(names));
        }

        private static void AppendInstalledDescendants(StringBuilder sb, BodyPartSlot slot, int start) {
            var attached = slot.AttachedParts;
            if (Arrays.IsEmpty(attached)) { return; }
            for (int i = 0; i < attached.Length; i++) {
                var child = attached[i];
                if (!child.IsPartRemoved) {
                    if (sb.Length > start) {
                        sb.Append(", ");
                    }
                    sb.Append(child.NameT);
                }
                AppendInstalledDescendants(sb, child, start);
            }
        }

        internal static bool IsSingleSlotOp(SurgicalOpType opType) {
            return opType == SurgicalOpType.Install
                || opType == SurgicalOpType.Swap
                || opType == SurgicalOpType.Remove;
        }

        // In a swap the removed part leaves the body either way, so a complication that only
        // damages it is no failed surgery: the replacement still goes in. Reported as a failure,
        // it read as if the new part was ruined (STA-1780). BodyPartSlot.TryRemovePart logs the
        // damage on the removed part instead. Not for a brain or core: the removed one holds
        // the mind, and damage to it is always news.
        internal static bool IsSwappedOutPartDamage(SurgicalOpFailureType failType, bool isExtract,
                SurgicalOpType opType, BodyPartSlot slot) {
            if (!isExtract || opType != SurgicalOpType.Swap || slot.HoldsMind) { return false; }
            return failType == SurgicalOpFailureType.WoundPart
                || failType == SurgicalOpFailureType.CripplePart
                || failType == SurgicalOpFailureType.DestroyPart;
        }

        internal static string DescribeFailure(SurgicalOpFailureType failType, BodyPartDef part) {
            switch (failType) {
                case SurgicalOpFailureType.WoundPart:
                    return T.SurgicalOpFailedWound_V1.F(part.Def.CodexLink);
                case SurgicalOpFailureType.WeldPart:
                    return (part.IsBiological ? T.SurgicalOpFailedFuse_V1 : T.SurgicalOpFailedWeld_V1)
                        .F(part.Def.CodexLink);
                case SurgicalOpFailureType.CripplePart:
                    return T.SurgicalOpFailedCripple_V1.F(part.Def.CodexLink);
                case SurgicalOpFailureType.DestroyPart:
                    return T.SurgicalOpFailedDestroyPart_V1.F(part.Def.CodexLink);
                case SurgicalOpFailureType.DestroySlot:
                    return T.SurgicalOpFailedDestroySlot;
                case SurgicalOpFailureType.Fatality:
                    return T.SurgicalOpFailedFatality;
                case SurgicalOpFailureType.Rejection:
                    return T.SurgicalOpFailedRejection_V1.F(part.Def.CodexLink);
                default:
                    return string.Empty;
            }
        }
    }
}
