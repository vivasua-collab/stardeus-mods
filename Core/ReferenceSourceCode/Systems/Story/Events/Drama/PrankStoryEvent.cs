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

namespace Game.Systems.Story {
    public sealed class PrankStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new PrankStoryEvent());
        }

        public const string EventId = "Prank";
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
                if (IsClusterCompatible(ctx, cl, maxBeings: 6)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsClusterCompatible(StoryContext ctx, BeingCluster cluster,
                int maxBeings) {
            if (cluster.IsExpedition) { return false; }
            var cnt = cluster.FriendlySociable.Count;
            if (cnt < 2 || cnt > maxBeings) { return false; }
            // prankster must be idle
            if (cluster.FriendlySociableIdle.Count < 1) {
                return false;
            }
            foreach (var b in cluster.FriendlySociable) {
                if (CanBePranked(b)) {
                    return true;
                }
            }
            return false;
        }

        private bool CanBePranked(BeingData b) {
            return !HasForceAssignedGoal(b)
                && b.Being?.AIAgent.CurrentAction?.IsMobile != true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.TargetOther is not List<BeingCluster> clusters) {
                data.FailEvent(s,
                    $"TargetOther is not a list of being clusters: {data.TargetOther}");
                return;
            }
            var rng = data.SeededRng;
            var cluster = clusters.Where(cl => IsClusterCompatible(ctx, cl,
                    maxBeings: 100))
                .OrderBy(cl => rng.Value).FirstOrDefault();
            if (cluster == null || !IsClusterCompatible(ctx, cluster, maxBeings: 100)) {
                data.FailEvent(s, $"Selected cluster is not compatible, no pair! {cluster}");
                return;
            }
            BeingData initiator = cluster.FriendlySociableIdle
                .Where(b => !HasForceAssignedGoal(b))
                .OrderBy(b => rng.Value).FirstOrDefault();
            if (initiator == null) {
                data.FailEvent(s, "Couldn't pick initiator for prank", isHardFailure: false);
                return;
            }
            BeingData target = cluster.FriendlySociable.Where(b => b != initiator && CanBePranked(b))
                .OrderBy(b => rng.Value).FirstOrDefault();
            if (target == null) {
                // target started moving?
                target = cluster.FriendlySociable.Where(b =>
                        b != initiator && !HasForceAssignedGoal(b))
                    .OrderBy(b => rng.Value).FirstOrDefault();
            }
            if (target == null) {
                data.FailEvent(s, "Couldn't pick target for prank", isHardFailure: false);
                return;
            }

            data.TargetBeing = initiator;
            data.AddParticipant(initiator);
            data.AddParticipant(target);
            if (BeginPrank(ctx, data)) {
                s.Sys.Log.AddLine(initiator, $"{T.VerbPranking}: {Texts.LinkToBeing(target)}");
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start a prank", isHardFailure: false);
                }
            }
        }

        private bool BeginPrank(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CJester,
                    T.PrankingSomeone_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.Pranked, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Warn("Failed assigning prank goal to: {0} | {1} | {2}",
                    m1.Owner, m1.Owner.Body, m1.Owner.Being?.AIAgent?.CurrentAction);
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
            if (curAct?.IdH != AIActionPrank.ActIdH) {
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
            var op1 = s.Query.GetOpinionAbout.Ask(m1, m2);
            var op2 = s.Query.GetOpinionAbout.Ask(m2, m1);
            var chanceGood = (GoodOutcomeChance(op1.SocialAffinity, op1.Hate)
                + GoodOutcomeChance(op2.SocialAffinity, op2.Hate)) / 2f;

            var wasGood = rng.Chance(chanceGood);
            var attitudeChangeAmt = 0.05f;
            var hateChange = -0.03f;
            var friendshipChange = 0.03f;
            string desc;
            if (!wasGood) {
                attitudeChangeAmt *= -1f;
                hateChange *= -1f;
                friendshipChange *= -1f;
                desc = T.PrankOutcomeBad_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            } else {
                desc = T.PrankOutcomeGood_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            }

            CreateMentalEffect(m1.Owner, T.PrankedSomeone_V1.F(m2.LinkToShortName), desc, wasGood);
            CreateMentalEffect(m2.Owner, T.GotPrankedBy_V1.F(m1.LinkToShortName), desc, wasGood);

            Notify(ctx, data, desc);

            var soc = SocialInteraction.For(data);
            soc.WithAttitude(attitudeChangeAmt)
                .WithFriendship(friendshipChange)
                .WithHate(hateChange);

            ctx.S.Query.RegisterSocialInteraction.Ask(soc);

            if (!wasGood) {
                var chanceRetaliate = (-op2.SocialAffinity * 0.25f) + op2.Hate * 0.5f;
                chanceRetaliate = Mathf.Lerp(0.05f, 0.90f, chanceRetaliate);
                if (!m2.Owner.Species.IsAnimal && m1.Owner.Species.IsAnimal) {
                    // non-animals will tolerate animals more
                    chanceRetaliate *= 0.1f;
                }
                if (rng.Chance(chanceRetaliate)) {
                    if (m2.Jobs.CanDo(JobTypeIdH.Melee) && !m2.Owner.Body.IsIncapacitated) {
                        ctx.S.Query.StartFriendlyFightBetween.Ask(m2.Owner.Being.AIAgent.CombatUnit,
                            m1.Owner.Being.AIAgent.CombatUnit);
                        return;
                    }
                }
            }
            if (rng.Chance(0.25f)) {
                var chaseData = StoryEventData.CreateFor(StoryEventDef.Get("Social/Chase"));
                chaseData.Seed = rng.Int;
                chaseData.AddParticipant(m2);
                chaseData.AddParticipant(m1);
                s.Query.StartStoryEvent.Ask(chaseData);
            }
        }

        // Neutral pairs land at 60%, best friends at 95%, nemeses at 25%. Affinity used to be
        // scaled by a quarter, which capped even best friends at 42.5%.
        public static float GoodOutcomeChance(float socialAffinity, float hate) {
            return Mathf.Lerp(0.25f, 0.95f, 0.5f + socialAffinity * 0.5f - hate * 0.5f);
        }

        private void CreateMentalEffect(BeingData b1, string text, string desc, bool wasGood) {
            var icon = IconId.CJester;
            var str = wasGood ? 5 : -7;

            b1.Being?.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(b1.Being.PosIdx,
                icon, G.Ticks, Consts.TicksPer8Hours, text, str).WithTooltip(desc));
        }

        private void Notify(StoryContext ctx, StoryEventData data, string outcome) {
            BuildLogEntry(data, outcome);
            ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
            // data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
            //     priority: Priority.Low,
            //     showPopup: false, showFanfare: false, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data, string outcome) {
            var minds = data.Participants;
            var desc = T.PrankDesc_V2.F(
                minds[0].LinkToShortName, minds[1].LinkToShortName);

            data.CreateLogEntry(IconId.CJester, desc, outcome);
        }
    }
}
