using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Conditions {
    public sealed class ConditionHacked : Condition {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionHacked());
        }
        public override Priority Importance => Priority.Third;
        public override int MoodSourceH => MoodSourceIdH.Hacked;
        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier hackedMod;
        private static StatModifier HackedMod => hackedMod ??= StatModifier.Max(0.49f, T.VerbHacked);
        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.PauseWhenBeingHidden;
            cond.Flags |= ConditionFlags.TickAfterDeath;
            if (cond.Owner.Species.IsBiological) {
                cond.Flags |= ConditionFlags.RemoveOnResurrect;
            } else {
                cond.Owner.StatsBeing.AIAlignment.AddModifier(HackedMod);
            }
        }

        public override bool ShowOnHoverFor(Being b) {
            return b.IsAlive;
        }

        public override void OnEnd(ConditionState cond, long ticks) {
            cond.Owner.StatsBeing.AIAlignment.RemoveModifier(HackedMod, false);
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            OnStart(cond, ticks);
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (!cond.Owner.Body.IsAlive) {
                cond.Owner.RemoveCondition(s, IdH);
                return;
            }
            bool hasUsableImplant = false;
            // Only robots are hacked now. An organic still carrying the condition from an
            // older save (it used to come through an Obey Chip) is let go (STA-1920)
            if (cond.Owner.Species.IsBiological) {
                cond.Owner.RemoveCondition(s, IdH);
                return;
            }
            // firewall chip installed
            if (BodyUtils.HasWorkingImplant(cond.Owner,
                    DefIdsH.ObjPartsImplantFirewallChip)) {
                cond.Owner.RemoveCondition(s, IdH);
                return;
            }
            // core removed, abort hacking
            if (cond.Owner.Body.MindSlot.IsPartRemoved) {
                cond.Owner.RemoveCondition(s, IdH);
                return;
            }
            var hackRate = 1f / (Tunables.HackedSeverityIncreaseDurationHours * Consts.TicksPerHour);
            if (!Advance(s, ticks, ConditionIdH.Hacked,
                    cond.Owner, 1,
                    hackRate,
                    0f,
                    false,
                    Severity.Irreparable)) {
                return;
            }
            if (cond.Severity != Severity.Irreparable) { return; }
            // S.Situation.AddTension(1f, "Hacked");
            hasUsableImplant |= BodyUtils.HasWorkingImplant(cond.Owner,
                DefIdsH.ObjPartsImplantObeyChip);
            BodyPartSlot part = null;
            // Seed on tick + victim, so a batch of bots hacked on the same tick
            // (Signal Intrusion works on a multi-selection) does not share one outcome.
            // Mixed through a real hash: nearby seeds (consecutive ids) give nearly the
            // same first draw, which made the whole batch flip or stay together.
            var rng = new Rng(Unity.Mathematics.math.hash(
                new Unity.Mathematics.int2((int) cond.StartTick, (int) cond.Owner.Id)) | 1u);

            if (cond.Owner.Mind?.Faction?.IsPlayer != true) {
                // Signal Intrusion, ordered by the player
                ResolveIntrusion(s, cond.Owner, rng, ticks);
                cond.Owner.RemoveCondition(s, IdH);
                return;
            }

            if (hasUsableImplant) {
                cond.Owner.Body.TryGetSlot(BodyPartSlotIdH.Implant, out part);
            }

            var changeFaction = rng.Chance(0.5f);

            // fry the minicore / brain / chip
            part ??= cond.Owner.Body.MindSlot;
            var dmg = Severities.Random(Severity.Critical, Severity.Irreparable, rng);
            if (hasUsableImplant) {
                // always cook the obey chip
                dmg = Severity.Irreparable;
            }
            if (!changeFaction || hasUsableImplant) {
                part?.TryAddOrUpdateWound(NamedEntity.For(T.VerbHacking), DamageType.Energy, dmg,
                    ticks, 1f, conditionId: IdH);
            }

            if (changeFaction) {
                cond.Owner.Mind?.SetFaction(FlipTargetFor(cond.Owner));
            }

            if (!cond.Owner.Body.IsAlive) {
                cond.Owner.Mind.OnDeath(T.CondHacked, ticks);
            }

            cond.Owner.RemoveCondition(s, IdH);
        }

        // A whole downed raid can be hacked with one multi-selection, so the player side
        // must not convert like the Virus does: it fails some of the time, converts only
        // occasionally (rarer the sooner after the last conversion), and otherwise burns
        // the core out.
        private static void ResolveIntrusion(GameState s, BeingData victim, Rng rng,
                long ticks) {
            if (rng.Chance(Tunables.HackFailChance)) {
                s.Sys.Log.AddLine(T.HackFailed_V1.F(Texts.LinkToBeing(victim)));
                return;
            }
            var combat = s.Sys.Combat;
            var days = combat.LastHackConversionTick <= 0
                ? float.MaxValue
                : (ticks - combat.LastHackConversionTick) / (float) Consts.TicksPerDay;
            var convertChance = CurveDef.FastGet(
                CurveIdH.Beings_DaysSinceHackConversion_ConvertChance).Evaluate(days);
            if (rng.Chance(convertChance)) {
                combat.LastHackConversionTick = ticks;
                victim.Mind?.SetFaction(FlipTargetFor(victim));
                return;
            }
            victim.Body.MindSlot?.TryAddOrUpdateWound(NamedEntity.For(T.VerbHacking),
                DamageType.Energy, Severity.Irreparable, ticks, 1f,
                conditionId: ConditionIdH.Hacked);
            if (!victim.Body.IsAlive) {
                victim.Mind?.OnDeath(T.CondHacked, ticks);
            }
        }

        /// <summary>
        /// Hacking runs in both directions. The Virus takes our beings; Signal
        /// Intrusion takes theirs. Whoever the victim currently belongs to, they end
        /// up on the other side - deriving it from the victim keeps the Virus story
        /// event working exactly as before without storing anything on the condition.
        /// </summary>
        private static FactionDef FlipTargetFor(BeingData victim) {
            if (victim.Mind?.Faction?.IsPlayer == true) {
                return FactionDef.FastGet(FactionIdH.Virus);
            }
            return FactionDef.Player;
        }

        private ConditionHacked() : base(ConditionId.Hacked, IconId.IconsFactionVirus) {
            PreventsControl = true;
        }
    }
}
