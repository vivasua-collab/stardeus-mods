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
    public sealed class MakeLoveStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new MakeLoveStoryEvent());
        }

        public const string EventId = "MakeLove";
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
            var pairs = new Dictionary<(BeingData, BeingData), float>();

            List<BeingData> candidates = cluster.FriendlySociable;
            FindCandidates(pairs, candidates);

            if (ctx.Rng.TryWeightedFrom(pairs, out pair)) {
                return true;
            }
            return false;
        }

        private const float MinLibidoToInitiate = 0.3f;

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
                    if (!b2.Mind.Bonds.Opinions.TryGetValue(b1.Mind.Id, out var op2)) {
                        continue;
                    }
                    if (!op1.AreLoversWith(op2)) {
                        continue;
                    }
                    if (op1.SexualAttraction <= 0f || op2.SexualAttraction <= 0f) {
                        continue;
                    }
                    // At least one partner must have sufficient libido to initiate
                    if (b1.Body.MaxLibido < MinLibidoToInitiate
                            && b2.Body.MaxLibido < MinLibidoToInitiate) {
                        continue;
                    }
                    var love = op1.Love + op2.Love + 0.01f;
                    pairs.TryAdd((b1, b2), love);
                }
            }
        }

        private bool CanParticipate(BeingData b) {
            if (b.Mind.Bonds == null) { return false; }
            if (HasForceAssignedGoal(b)) { return false; }
            if (b.Being?.AIAgent.Goal?.IsNonInterruptable == true) {
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
            var s = ctx.S;
            var forceSelected = A.InstantBuild;
            #if UNITY_EDITOR
            forceSelected = true;
            #endif
            if (forceSelected) {
                var selected = s.Query.GetSelectedBeings.Ask();
                if (selected.Count == 2) {
                    var b1 = selected[0].Data;
                    var b2 = selected[1].Data;
                    D.Warn("Forcing make love between {0} and {1}", b1, b2);
                    DoMakeLove(ctx, data, s, (b1, b2));
                    return;
                }
            }
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
                D.ErrSoft("Failed finding good candidates for a date in clusters: {0}. Will fall back to all beings",
                    cluster);
                // fallback
                var candidates = Caches.BeingDataList;
                foreach (var b in ctx.S.Beings.Alive) {
                    if (b.Data.IsPlayer && b.Data.Mind.Health != null) {
                        candidates.Add(b.Data);
                    }
                }
                var pairs = new Dictionary<(BeingData, BeingData), float>();
                FindCandidates(pairs, candidates);
                if (!ctx.Rng.TryWeightedFrom(pairs, out pair)) {
                    data.FailEvent(s, "No compatible being cluster for a date. fallback failed");
                    return;
                }
            }
            DoMakeLove(ctx, data, s, pair);
        }

        private void DoMakeLove(StoryContext ctx, StoryEventData data, GameState s, (BeingData, BeingData) pair) {
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
            if (BeginMakeLove(ctx, data)) {
                data.SetState(s, StoryEventState.Ongoing);
                Notify(ctx, data);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start a date");
                }
            }
        }

        private bool BeginMakeLove(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CHeart,
                    T.MakeLoveWith_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.MadeLoveWith_Lead, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Err("Failed assigning make love goal (lead) to: {0}", m1);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            var g2 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CHeart,
                    T.MakeLoveWith_V1.F(m1.LinkToShortName),
                    m2.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m1.Owner.Id)
                .WithWorldToDesired(AIVarsH.MadeLoveWith_Follow, false, true);
            if (!s.Query.ForceGoal.Ask(g2, false)) {
                D.Err("Failed assigning make love goal (follow) to: {0}", m2);
                AIGoal.Cancel(g1, G.Ticks, T.Error);
                return false;
            } else {
                g2.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);
            g2.WithInterrupt(1, 0);

            return true;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var mind = Arrays.SafeGetFast(data.Participants, 0);
            var curAct = mind?.Owner?.Being?.AIAgent.CurrentAction;
            if (mind?.Owner?.Being == null
                    || (curAct != null && curAct is not IAIActionMakeLove)) {
                data.SetState(ctx.S, StoryEventState.Ending);
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            // someone left the ship before it concluded
            if (!data.HasParticipantsAboard(2)) { return; }
            var s = ctx.S;
            var rng = data.SeededRng;
            var duration = ctx.Ticks - data.StartTick;
            var attitudeChangeAmt = 0.3f * (duration / (float) Consts.TicksPer2Hours);
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];
            var op1 = s.Query.GetOpinionAbout.Ask(m1, m2);
            var op2 = s.Query.GetOpinionAbout.Ask(m2, m1);
            var chanceGood1 = (op1.SocialAffinity * 0.25f)
                + op1.SexualAttraction * 0.5f + op1.Love * 0.5f - op1.Hate * 0.5f;
            var chanceGood2 = (op2.SocialAffinity * 0.25f)
                + op2.SexualAttraction * 0.5f + op2.Love * 0.5f - op2.Hate * 0.5f;
            chanceGood1 = Mathf.Lerp(0.25f, 0.90f, chanceGood1);
            chanceGood2 = Mathf.Lerp(0.25f, 0.90f, chanceGood2);
            var chanceGood = (chanceGood1 + chanceGood2) / 2f;
            int mentalEffectDur = (int) Mathf.Clamp(duration,
                Consts.TicksPerHour, Consts.TicksPer4Hours) * 2;
            D.Warn("Chance good: {0}/{1} -> {2}", chanceGood1, chanceGood2, chanceGood);
            var wasGood = rng.Chance(chanceGood);
            var loveChange = 0.01f;
            if (!wasGood) {
                attitudeChangeAmt *= -1f;
                loveChange *= -1f;
                data.LogEntry.Icon = IconId.CHeartBroken;
                data.LogEntry.Desc = T.MakeLoveBadDesc_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            } else {
                data.LogEntry.Icon = IconId.CHeart;
                data.LogEntry.Desc = T.MakeLoveGoodDesc_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            }
            var soc = SocialInteraction.For(data)
                .WithAttitude(attitudeChangeAmt)
                .WithLove(loveChange);
            CreateMentalEffect(m1.Owner, wasGood, data);
            CreateMentalEffect(m2.Owner, wasGood, data);

            ctx.S.Query.RegisterSocialInteraction.Ask(soc);

            if (!wasGood) {
                var chanceRetaliate = (-op2.SocialAffinity * 0.25f) + op2.Hate * 0.5f;
                chanceRetaliate = Mathf.Lerp(0.05f, 0.33f, chanceRetaliate);
                if (rng.Chance(chanceRetaliate)) {
                    if (m2.Jobs.CanDo(JobTypeIdH.Melee) && !m2.Owner.Body.IsIncapacitated) {
                        ctx.S.Query.StartFriendlyFightBetween.Ask(m2.Owner.Being.AIAgent.CombatUnit,
                            m1.Owner.Being.AIAgent.CombatUnit);
                        return;
                    }
                }
            }
        }

        private void CreateMentalEffect(BeingData b1, bool wasGood, StoryEventData data) {
            var change = wasGood ? 15 : -10;
            b1.Being?.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(b1.Being.PosIdx,
                data.LogEntry.Icon, G.Ticks, Consts.TicksPerDay, T.MakeLoveTitle, change)
                    .WithTooltip(data.LogEntry.Desc));
        }

        private void Notify(StoryContext ctx, StoryEventData data) {
            BuildLogEntry(data);
            ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
            // data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
            //     priority: Priority.Low,
            //     showPopup: false, showFanfare: false, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data) {
            var minds = data.Participants;
            var desc = T.MakeLoveDesc_V2.F(
                minds[0].LinkToShortName, minds[1].LinkToShortName);
            data.CreateLogEntry(IconId.CHeart, T.MakeLoveTitle, desc);
        }
    }
}
