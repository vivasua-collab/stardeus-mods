using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.CodeGen;
using UnityEngine;
using Game.Utils;
using Game.Systems.Drama;
using Game.Systems.Creatures;
using System.Linq;
using KL.Utils;
using System;
using KL.Grid;
using Game.Systems.AI;
using Game.Systems.Mental;
using KL.Randomness;
using Game.Systems.BodyParts;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class ConfessLoveStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new ConfessLoveStoryEvent());
        }

        public const string EventId = "ConfessLove";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Situation.Colonists > 1;
        }

        public override bool CheckHook(StoryContext ctx, StoryEventDef def, StoryEventHook hook) {
            if (hook.Target is not List<BeingCluster> clusters) {
                D.Warn("Hook target not list of being clusters! {0}", hook.Target);
                return false;
            }
            foreach (var cl in clusters) {
                if (IsClusterCompatible(ctx, cl)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsClusterCompatible(StoryContext ctx, BeingCluster cluster) {
            if (cluster.IsExpedition) { return false; }
            // stabber must be idle
            if (cluster.FriendlySociableIdle.Count == 0) { return false; }
            for (int i = cluster.FriendlySociable.Count - 1; i >= 0; i--) {
                var being = cluster.FriendlySociable[i];
                if (HasMotive(being)) {
                    return true;
                }
            }
            return false;
        }
        public const float MinLibidoToInitiate = 0.3f;

        private bool HasMotive(BeingData being) {
            if (being.Being == null) { return false; }
            if (HasForceAssignedGoal(being)) { return false; }
            if (being.Mind.Health == null) { return false; }
            if (being.Mind.Bonds == null) { return false; }
            if (being.Mind.Bonds.Opinions.Count == 0) { return false; }
            if (!being.HasAbility(AbilityIdH.Vocalize)) { return false; }
            if (being.Body.MaxLibido < MinLibidoToInitiate) { return false; }
            // already has a lover
            var op1Imp = being.Mind.Bonds.Important;
            var isPromiscuous = being.Mind.Traits.IsPromiscuous;
            var hasLover = op1Imp.ContainsKey(BeingOpinionFlags.Lover);
            if (hasLover && !isPromiscuous) { return false; }
            var hasSpouse = op1Imp.ContainsKey(BeingOpinionFlags.Spouse);
            if (hasSpouse && !isPromiscuous) { return false; }
            if (being.Mind.Bonds.Important.TryGetValue(BeingOpinionFlags.Crush, out var crushes)) {
                for (int i = crushes.Length - 1; i >= 0; i--) {
                    if (IsValidTarget(being, crushes[i])) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsValidTarget(BeingData being, Mind crush) {
            if (crush.Health == null) { return false; }
            if (crush.Owner == null) { return false; }
            if (crush.Owner.Being == null) { return false; }
            // a confession is a chat, see BeingChat.WillChat
            if (!crush.Owner.HasAbility(AbilityIdH.Hear)) { return false; }
            if (HasForceAssignedGoal(crush.Owner)) { return false; }
            // Require initiator to be genuinely attracted (orientation-compatible)
            if (!being.Mind.Bonds.Opinions.TryGetValue(crush.Id, out var initiatorOp)
                    || initiatorOp.SexualAttraction <= 0f) {
                return false;
            }
            if ((crush.Owner.StateFlags & BeingState.IncapableOfCombat) != BeingState.Uninitialized) {
                return false;
            }
            if (crush.Owner.Body.IsIncapacitated) { return false; }
            // Not while being carried, e.g. to surgery (STA-1258)
            if (crush.Owner.Being.IsCarriedByAny) { return false; }
            if (crush.Owner.Being.AIAgent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            var op2Imp = crush.Bonds.Important;
            var hasSpouse = op2Imp.ContainsKey(BeingOpinionFlags.Spouse);
            var hasLover = op2Imp.ContainsKey(BeingOpinionFlags.Lover);
            if (hasSpouse || hasLover) {
                if (Rng.UChance(0.9f)) {
                    return false;
                }
            }
            if (!being.Safety.IsActuallySafeAt(crush.Owner.Being.PosIdx)) {
                return false;
            }
            if (being.Being.Motion.IsGrounded
                    && !A.S.Sys.Areas.CanReachGrounded(being.Being.PosIdx, crush.Owner.Being.PosIdx)) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.TargetOther is not List<BeingCluster> clusters) {
                data.FailEvent(s,
                    $"TargetOther is not a list of being clusters: {data.TargetOther}");
                return;
            }
            var rng = data.SeededRng;
            var cluster = clusters.Where(cl => IsClusterCompatible(ctx, cl))
                .OrderBy(cl => rng.Value).FirstOrDefault();
            if (cluster == null) {
                data.FailEvent(s, $"Selected cluster is not compatible, no pair! {cluster}");
                return;
            }

            var crushes = Caches.BeingDataList;
            crushes.Clear();
            BeingData initiator = null;
            for (int i = cluster.FriendlySociable.Count - 1; i >= 0; i--) {
                var being = cluster.FriendlySociable[i];
                if (HasMotive(being)) {
                    if (being.Mind.Bonds.Important.TryGetValue(BeingOpinionFlags.Crush, out var crs)) {
                        for (int j = 0; j < crs.Length; j++) {
                            var cr = crs[j];
                            if (IsValidTarget(being, cr)) {
                                crushes.Add(cr.Owner);
                            }
                        }
                    }
                }
                if (crushes.Count > 0) {
                    initiator = being;
                    break;
                }
            }

            if (initiator == null) {
                data.FailEvent(s, "Couldn't pick initiator for confession", isHardFailure: false);
                return;
            }
            if (crushes.Count == 0) {
                data.FailEvent(s, "Couldn't pick target for confession", isHardFailure: false);
                return;
            }
            var target = rng.From(crushes);

            data.TargetBeing = initiator;
            data.AddParticipant(initiator);
            data.AddParticipant(target);
            if (BeginConfession(ctx, data)) {
                s.Sys.Log.AddLine(initiator,
                    T.ActConfessingLove_V1.F(Texts.LinkToBeing(target)));
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start a confession");
                }
            }
        }

        private bool BeginConfession(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CHeart,
                    T.ActConfessingLove_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.ConfessedLove, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Err("Failed assigning confess love goal to: {0}", m1);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);

            return true;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var mind = Arrays.SafeGetFast(data.Participants, 0);
            var curAct = mind?.Owner?.Being?.AIAgent.CurrentAction;
            if (curAct?.IdH != AIActionConfessLove.ActIdH) {
                data.SetState(ctx.S, StoryEventState.Ending);
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            // someone left the ship before it concluded
            if (!data.HasParticipantsAboard(2)) { return; }
            var s = ctx.S;
            var rng = data.SeededRng;

            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            bool reciprocated;
            bool ignored = false;

            string mentalEffectM1Title;
            string mentalEffectM2Title = T.ReceivedLoveConfession;
            string outcomeDesc;

            float attitudeChangeAmt;
            float loveChangeAmt;
            float changeWeightM1;
            float changeWeightM2;

            sbyte mentalEffectM1;
            sbyte mentalEffectM2;
            int effectDurationM1;
            int effectDurationM2;
            string iconM1;
            string iconM2;

            // target opinion about confessor
            var op1 = s.Query.GetOpinionAbout.Ask(m1, m2);
            var op2 = s.Query.GetOpinionAbout.Ask(m2, m1);
            float op1LoveBefore = op1.Love;
            reciprocated = op2.Flags.HasFlag(BeingOpinionFlags.Crush);

            var op2Imp = op2.TargetMind.Bonds.Important;
            var hasSpouse = op2Imp.ContainsKey(BeingOpinionFlags.Spouse);
            var hasLover = op2Imp.ContainsKey(BeingOpinionFlags.Lover);
            if (reciprocated) {
                // already has a lover
                if (hasSpouse || hasLover) {
                    if (!op2.TargetMind.Traits.IsPromiscuous && rng.Chance(0.9f)) {
                        reciprocated = false;
                    }
                }
            }

            if (!reciprocated) {
                // still a chance later
                ignored = op2.Love > 0.1f;
                iconM1 = IconId.CHeartBroken;
                if (ignored) {
                    iconM2 = IconId.CHeart;

                    attitudeChangeAmt = 0.2f;
                    loveChangeAmt = 0.15f;
                    changeWeightM1 = -1f;
                    changeWeightM2 = 1f;

                    effectDurationM1 = Consts.TicksPerDay;
                    effectDurationM2 = Consts.TicksPerDay;
                    mentalEffectM1 = -5;
                    mentalEffectM2 = 5;
                    outcomeDesc = T.ConfessLoveOutcomeIgnored_V2;
                    mentalEffectM1Title = T.ConfessedLoveIgnored;
                } else {
                    iconM2 = IconId.CHeartBroken;
                    attitudeChangeAmt = -0.1f;
                    loveChangeAmt = -0.25f;
                    changeWeightM1 = 1f;
                    changeWeightM2 = 0.05f;

                    effectDurationM1 = Consts.TicksPerDay;
                    effectDurationM2 = Consts.TicksPer4Hours;
                    mentalEffectM1 = -15;
                    mentalEffectM2 = -5;

                    outcomeDesc = T.ConfessLoveOutcomeRejected_V2;
                    mentalEffectM1Title = T.ConfessedLoveRejected;
                }
            } else {
                attitudeChangeAmt = 0.2f;
                loveChangeAmt = 0.2f;
                changeWeightM1 = 1f;
                changeWeightM2 = 1f;
                iconM1 = IconId.CHeart;
                iconM2 = IconId.CHeart;
                mentalEffectM1 = 20;
                mentalEffectM2 = 20;

                effectDurationM1 = Consts.TicksPerDay;
                effectDurationM2 = Consts.TicksPerDay;
                outcomeDesc = T.ConfessLoveOutcomeAccepted_V2;
                mentalEffectM1Title = T.ConfessedLoveAccepted;
            }

            string desc = outcomeDesc.F(m1.LinkToShortName, m2.LinkToShortName);

            CreateMentalEffect(m1.Owner, mentalEffectM1Title, desc, mentalEffectM1, iconM1, effectDurationM1);
            CreateMentalEffect(m2.Owner, mentalEffectM2Title, desc, mentalEffectM2, iconM2, effectDurationM2);

            Notify(ctx, data, iconM1, desc);

            var soc = SocialInteraction.For(data.LogEntry);
            soc.WithAttitude(attitudeChangeAmt)
                .WithLove(loveChangeAmt)
                .With(m1.Owner, changeWeightM1)
                .With(m2.Owner, changeWeightM2);

            ctx.S.Query.RegisterSocialInteraction.Ask(soc);

            if (reciprocated) {
                BeingOpinion.MakeLovers(s, op1, op2);
            } else if (!ignored) {
                if (m1.Owner.Body.TryGetSlot(BodyPartSlotIdH.Heart, out var heart)) {
                    var scar = new Scar();
                    scar.Severity = Severities.For(op1LoveBefore, false);
                    scar.CreatedAt = G.Ticks;
                    scar.Source = T.ScarLoveConfessionRejection_V1.F(m2.LinkToShortName);
                    scar.Type = DamageType.Piercing;
                    heart.AddScar(scar);
                }
                var chanceRetaliate = (-op2.SocialAffinity * 0.25f) + op2.Hate * 0.5f;
                chanceRetaliate = Mathf.Lerp(0f, 0.1f, chanceRetaliate);
                if (rng.Chance(chanceRetaliate)) {
                    if (m2.Jobs.CanDo(JobTypeIdH.Melee)) {
                        ctx.S.Query.StartFriendlyFightBetween.Ask(
                            m2.Owner.Being.AIAgent.CombatUnit,
                            m1.Owner.Being.AIAgent.CombatUnit);
                    }
                }
            }
        }

        private void CreateMentalEffect(BeingData b1, string text, string desc,
                sbyte effect, string icon, int duration) {
            b1.Being?.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(b1.Being.PosIdx,
                icon, G.Ticks, duration, text, effect).WithTooltip(desc));
        }

        private void Notify(StoryContext ctx, StoryEventData data, string icon, string outcome) {
            data.CreateLogEntry(icon, T.NounLoveConfession, outcome);
            data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
                type: NotificationType.Info,
                showPopup: false, addLogLine: true);
        }
    }
}
