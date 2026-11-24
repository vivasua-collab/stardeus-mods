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
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class WeddingStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new WeddingStoryEvent());
        }

        public const string EventId = "Wedding";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Situation.Colonists > 1;
        }
        private readonly Dictionary<(BeingData, BeingData), float> pairs = new();

        public override bool CheckHook(StoryContext ctx, StoryEventDef def, StoryEventHook hook) {
            if (hook.Target is not List<BeingCluster> clusters) {
                D.Warn("Hook target not list of being clusters! {0}", hook.Target);
                return false;
            }
            foreach (var cl in clusters) {
                if (IsClusterCompatible(ctx, cl, out _, maxBeings: 6)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsClusterCompatible(StoryContext ctx, BeingCluster cluster,
                out (BeingData, BeingData) pair, int maxBeings) {
            pair = default;
            if (cluster.IsExpedition) { return false; }
            var cnt = cluster.FriendlySociable.Count;
            if (cnt < 2 || cnt > maxBeings) { return false; }

            List<BeingData> candidates = cluster.FriendlySociable;
            pairs.Clear();
            FindCandidates(pairs, candidates);

            if (ctx.Rng.TryWeightedFrom(pairs, out pair)) {
                return true;
            }
            return false;
        }

        private void FindCandidates(Dictionary<(BeingData, BeingData), float> pairs,
                List<BeingData> candidates) {
            foreach (var b1 in candidates) {
                if (!CanParticipate(b1)) { continue; }
                foreach (var b2 in candidates) {
                    if (b1.Id >= b2.Id) { continue; }
                    if (!CanParticipate(b2)) { continue; }
                    if (!b1.Mind.Bonds.Opinions.TryGetValue(b2.Mind.Id, out var op1)) {
                        continue;
                    }
                    var timeW = (float) op1.TicksInProximity / Consts.TicksPerMonth;
                    // haven't spent enough time together
                    if (timeW < 1f) {
                        continue;
                    }
                    if (!b2.Mind.Bonds.Opinions.TryGetValue(b1.Mind.Id, out var op2)) {
                        continue;
                    }
                    if (!op1.AreLoversWith(op2)) {
                        continue;
                    }
                    var love = op1.Love + op2.Love + timeW * 0.1f;
                    pairs.TryAdd((b1, b2), love);
                }
            }
        }

        private bool CanParticipate(BeingData b) {
            if (b.Mind.Bonds == null) { return false; }
            if (!b.Mind.Species.IsHumanoid) { return false; }
            if (HasForceAssignedGoal(b)) { return false; }
            if (b.Being?.AIAgent.Goal?.IsNonInterruptable == true) {
                return false;
            }
            if (!b.Mind.Bonds.Important.ContainsKey(BeingOpinionFlags.Lover)) {
                return false;
            }
            if (b.Mind.Bonds.Important.ContainsKey(BeingOpinionFlags.Spouse)) {
                return false;
            }
            // don't go out smelly
            if (b.HasNeed(NeedIdH.Hygiene) && b.StatsBeing.Hygiene.Value < 0.5f) {
                return false;
            }
            // skip tired
            if (b.HasNeed(NeedIdH.Sleep) && b.StatsBeing.Sleep.Value < 0.33f) {
                return false;
            }
            // don't shit yourself mid date
            if (b.HasNeed(NeedIdH.Toilet) && b.StatsBeing.Toilet.Value < 0.33f) {
                return false;
            }
            // don't go out hungry
            if (b.HasNeed(NeedIdH.Hunger) && b.StatsBeing.Hunger.Value < 0.33f) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            D.Warn("Wedding on start");
            var s = ctx.S;
            if (data.TargetOther is not List<BeingCluster> clusters) {
                data.FailEvent(s,
                    $"TargetOther is not a list of being clusters: {data.TargetOther}");
                return;
            }
            var rng = data.SeededRng;
            var cluster = clusters.Where(cl => IsClusterCompatible(ctx, cl, out _,
                    maxBeings: 100))
                .OrderBy(cl => rng.Value).FirstOrDefault();
            (BeingData, BeingData) pair = default;
            if (cluster != null) {
                if (!IsClusterCompatible(ctx, cluster, out pair, maxBeings: 100)) {
                    data.FailEvent(s, $"Selected cluster is not compatible, no pair! {cluster}");
                    return;
                }
            } else {
                D.ErrSoft("Failed finding good candidates for a wedding in clusters: {0}. Will fall back to all beings",
                    cluster);
                // fallback
                var candidates = Caches.BeingDataList;
                foreach (var b in ctx.S.Beings.Alive) {
                    if (b.Data.IsPlayer && b.Data.Mind.Health != null
                            && b.Data.Mind.Species.IsHumanoid) {
                        if (CanParticipate(b.Data)) {
                            candidates.Add(b.Data);
                        }
                    }
                }
                pairs.Clear();
                FindCandidates(pairs, candidates);
                if (!ctx.Rng.TryWeightedFrom(pairs, out pair)) {
                    data.FailEvent(s, "No compatible being cluster for a wedding. fallback failed");
                    return;
                }
            }
            BeingData initiator;
            BeingData target;
            if (ctx.Rng.Chance(0.5f)) {
                initiator = pair.Item1;
                target = pair.Item2;
            } else {
                initiator = pair.Item2;
                target = pair.Item1;
            }

            if (initiator.HasAbility(AbilityIdH.Fly) && !target.HasAbility(AbilityIdH.Fly)) {
                // let's make sure initiator will be less mobile
                var temp = initiator;
                initiator = target;
                target = temp;
            }

            data.TargetBeing = initiator;
            data.AddParticipant(initiator);
            data.AddParticipant(target);
            if (BeginWedding(ctx, data)) {
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start a wedding");
                }
            }
        }

        private bool BeginWedding(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CHeart,
                    T.DateWith_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.HadDateWith_Lead, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Err("Failed assigning date goal (lead) to: {0}", m1);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            var g2 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CHeart,
                    T.DateWith_V1.F(m1.LinkToShortName),
                    m2.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m1.Owner.Id)
                .WithWorldToDesired(AIVarsH.HadDateWith_Follow, false, true);
            if (!s.Query.ForceGoal.Ask(g2, false)) {
                D.Err("Failed assigning date goal (follow) to: {0}", m2);
                AIGoal.Cancel(g1, G.Ticks, T.Error);
                return false;
            } else {
                g2.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);
            g2.WithInterrupt(1, 0);

            NotifyDate(ctx, data);

            return true;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var mind = Arrays.SafeGetFast(data.Participants, 0);
            var curAct = mind?.Owner?.Being?.AIAgent.CurrentAction;
            if (mind?.Owner?.Being == null
                    || (curAct != null && curAct is not AIActionDate_Lead)) {
                data.SetState(ctx.S, StoryEventState.Ending);
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            // someone left the ship before it concluded
            if (!data.HasParticipantsAboard(2)) { return; }
            D.Warn("Wedding on end");
            var s = ctx.S;
            var rng = data.SeededRng;
            var attitudeChangeAmt = 0.2f;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];
            var op1 = s.Query.GetOpinionAbout.Ask(m1, m2);
            var op2 = s.Query.GetOpinionAbout.Ask(m2, m1);
            var loveChange = 0.1f;
            var desc = T.StatusSpouseAnnounce_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            data.LogEntry.Icon = IconId.CRings;
            data.LogEntry.Title = T.EvWeddingTitle;
            data.LogEntry.Desc = desc;
            data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
                showPopup: true, showFanfare: true, addLogLine: true);
            var soc = SocialInteraction.For(data)
                .WithAttitude(attitudeChangeAmt)
                .WithLove(loveChange);
            CreateMentalEffect(m1.Owner, data);
            CreateMentalEffect(m2.Owner, data);

            op1.SetFlag(BeingOpinionFlags.Spouse);
            op2.SetFlag(BeingOpinionFlags.Spouse);
            BeingBonds.AnnounceNew(s, m1, m2, BeingOpinionFlags.Spouse);

            ctx.S.Query.RegisterSocialInteraction.Ask(soc);

            var evt = StoryEventDef.Get("Social/Party");
            var dramaMem = s.Query.GetDramaMemory.Ask();
            dramaMem.RequestParty(T.NounWedding, m1);
            dramaMem.RequestParty(T.NounWedding, m2);
            ctx.ClearRecentSeen(evt, ctx.Ticks);
            ctx.PrioritizeEvent(evt, "Wedding");
        }

        private void CreateMentalEffect(BeingData b1, StoryEventData data) {
            b1.Being.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(b1.Being.PosIdx,
                data.LogEntry.Icon, G.Ticks, Consts.TicksPer3Days, data.LogEntry.Title, 35)
                    .WithTooltip(data.LogEntry.Desc));
        }

        protected override void Cleanup() {
            pairs.Clear();
        }

        private void NotifyDate(StoryContext ctx, StoryEventData data) {
            BuildLogEntry(data);
            data.AddEventNotification(ctx, IconId.CRings, data.LogEntry.Title,
                type: NotificationType.Info,
                showPopup: false, showFanfare: true, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data) {
            var minds = data.Participants;
            var desc = T.DateDesc_V2.F(
                minds[0].LinkToShortName, minds[1].LinkToShortName);
            data.CreateLogEntry(IconId.CHeart, T.NounDate, desc);
        }
    }
}
