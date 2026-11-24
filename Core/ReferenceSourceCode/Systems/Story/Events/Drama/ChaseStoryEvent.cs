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

namespace Game.Systems.Story {
    public sealed class ChaseStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new ChaseStoryEvent());
        }

        public const string EventId = "Chase";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var sit = ctx.S.Situation;
            var numNonBots = sit.Friendlies - sit.Workers;
            return numNonBots > 1;
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

        private bool CanChase(BeingData being) {
            if (being.Being == null) { return false; }
            if (HasForceAssignedGoal(being)) { return false; }
            if (being.Mind.Health == null) { return false; }
            if (being.Body.IsInjured) { return false; }
            if (being.Mind.Bonds == null) { return false; }
            if (being.Mind.Bonds.Opinions.Count == 0) { return false; }
            if (!being.IsFriendly) { return false; }
            return true;
        }

        private bool IsClusterCompatible(StoryContext ctx, BeingCluster cluster) {
            if (cluster.IsExpedition) { return false; }
            // stabber must be idle
            if (cluster.FriendlySociableIdle.Count == 0) { return false; }
            int count = 0;
            for (int i = cluster.FriendlySociable.Count - 1; i >= 0; i--) {
                var being = cluster.FriendlySociable[i];
                if (CanChase(being)) {
                    if (++count > 1) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsValidTarget(BeingData being, Mind target) {
            if (target.Owner == null) { return false; }
            if (target.Owner.Being == null) { return false; }
            if (HasForceAssignedGoal(target.Owner)) { return false; }
            if ((target.Owner.StateFlags & BeingState.IncapableOfCombat) != BeingState.Uninitialized) {
                return false;
            }
            if (target.Owner.Body.IsIncapacitated) { return false; }
            if (target.Owner.Being.AIAgent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            if (!being.Safety.IsActuallySafeAt(target.Owner.Being.PosIdx)) {
                return false;
            }
            if (being.Being.Motion.IsGrounded
                    && !A.S.Sys.Areas.CanReachGrounded(being.Being.PosIdx, target.Owner.Being.PosIdx)) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (Arrays.IsEmpty(data.Participants)) {
                if (data.TargetOther is not List<BeingCluster> clusters) {
                    data.FailEvent(s,
                        $"TargetOther is not a list of being clusters: {data.TargetOther}");
                    return;
                }
                var rng = data.SeededRng;
                var cluster = clusters.Where(cl => IsClusterCompatible(ctx, cl))
                    .OrderBy(cl => rng.Value).FirstOrDefault();

                PickParticipants(ctx, rng, cluster, out var chaser, out var target);

                if (chaser == null) {
                    data.FailEvent(s, "Couldn't pick initiator for chase", isHardFailure: false);
                    return;
                }
                if (target == null) {
                    data.FailEvent(s, "Couldn't pick prey for chase", isHardFailure: false);
                    return;
                }
                data.TargetBeing = chaser;
                data.AddParticipant(chaser);
                data.AddParticipant(target);
            } else {
                // participants are predefined
                data.TargetBeing = data.Participants[0].Owner;
            }

            if (BeginChasing(ctx, data)) {
                s.Sys.Log.AddLine(data.TargetBeing,
                    $"{T.VerbChasing}: {Texts.LinkToMind(data.Participants[1])}");
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start chasing", isHardFailure: false);
                }
            }
        }

        private bool PickParticipants(StoryContext ctx, Rng rng, BeingCluster cluster,
                out BeingData chaser, out BeingData target) {
            var wPrey = Caches.WeightedBeingData;
            wPrey.Clear();
            chaser = null;
            List<BeingData> candidates;
            if (cluster != null) {
                candidates = cluster.FriendlySociable;
            } else {
                ///
                candidates = Caches.BeingDataList;
                candidates.Clear();
                foreach (var b in ctx.S.Beings.Alive) {
                    if (b.AIAgent.CanWork && CanChase(b.Data)) {
                        candidates.Add(b.Data);
                    }
                }
                Arrays.Shuffle(rng, candidates);
            }
            for (int i = candidates.Count - 1; i >= 0; i--) {
                var being = candidates[i];
                if (!TryEnqueuePrey(ctx, wPrey, being)) {
                    continue;
                }
                if (wPrey.Count > 0) {
                    chaser = being;
                    break;
                }
            }
            if (chaser == null) {
                D.Warn("Couldn't pick chaser from beings: {0}", D.DumpCollection(candidates));
                target = null;
                return false;
            }
            if (wPrey.Count == 0) {
                // fallback
                foreach (var op in chaser.Mind.Bonds.Opinions.Values) {
                    var w = op.Hate + op.Friendship + 0.01f;
                    var mind = ctx.S.Query.FindMind.Ask(op.TargetMindId);
                    if (!IsValidTarget(chaser, mind)) { continue; }
                    wPrey.Add(mind.Owner, w);
                }
            }
            if (wPrey.Count == 0) {
                // final fallback
                foreach (var being in ctx.S.Beings.Alive) {
                    if (HasForceAssignedGoal(being.Data)) { continue; }
                    if (!IsValidTarget(chaser, being.Data.Mind)) {
                        wPrey.Add(being.Data, 1f);
                        continue;
                    }
                }
            }
            return rng.TryWeightedFrom(wPrey, out target);
        }

        private bool TryEnqueuePrey(StoryContext ctx, Dictionary<BeingData, float> wPrey, BeingData being) {
            if (!CanChase(being)) { return false; }
            foreach (var op in being.Mind.Bonds.Opinions.Values) {
                var mind = ctx.S.Query.FindMind.Ask(op.TargetMindId);
                if (IsValidTarget(being, mind)) {
                    wPrey.Add(mind.Owner, 0.01f + (op.Hate + op.Friendship)
                        * (mind.Owner.Species.IsAnimal ? 3f : 1f));
                }
            }

            return true;
        }

        private bool BeginChasing(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            return OrderChasing(ctx, s, m1, m2);
        }

        public static bool OrderChasing(StoryContext ctx, GameState s, Mind m1, Mind m2) {
            if (m1.Owner?.Being == null) { return false; }
            if (m2.Owner?.Being == null) { return false; }
            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CRunning,
                    T.ChasingTarget_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.Chased, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Warn("Failed assigning chasing goal to: {0} | {1}",
                    m1, m1.Owner.Being.AIAgent);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);

            var g2 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CRunning,
                    T.AvoidingSomeone_V1.F(m1.LinkToShortName),
                    m2.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m1.Owner.Id)
                .WithWorldToDesired(AIVarsH.ChaseAvoided, false, true);
            if (!s.Query.ForceGoal.Ask(g2, false)) {
                D.Warn("Failed assigning avoiding goal to: {0} | {1}",
                    m2, m2.Owner.Being.AIAgent);
                return false;
            } else {
                g2.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g2.WithInterrupt(1, 0);
            return true;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var mind = Arrays.SafeGetFast(data.Participants, 0);
            var curAct = mind?.Owner?.Being?.AIAgent.CurrentAction;
            if (curAct?.IdH != AIActionChase.ActIdH) {
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
            var chanceGood1 = (op1.SocialAffinity * 0.25f) - op1.Hate * 0.5f;
            var chanceGood2 = (op2.SocialAffinity * 0.25f) - op2.Hate * 0.5f;
            chanceGood1 = Mathf.Lerp(0.25f, 0.95f, chanceGood1);
            chanceGood2 = Mathf.Lerp(0.25f, 0.95f, chanceGood2);
            var chanceGood = (chanceGood1 + chanceGood2) / 2f;

            var wasGood = rng.Chance(chanceGood);
            var attitudeChangeAmt = 0.05f;
            var hateChange = -0.03f;
            var friendshipChange = 0.03f;
            string desc;
            if (!wasGood) {
                attitudeChangeAmt *= -1f;
                hateChange *= -1f;
                friendshipChange *= -1f;
                desc = T.ChaseOutcomeBad_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            } else {
                desc = T.ChaseOutcomeGood_V2.F(m1.LinkToShortName, m2.LinkToShortName);
            }

            CreateMentalEffect(m1.Owner, T.ChasedSomeone_V1.F(m2.LinkToShortName), desc, wasGood);
            CreateMentalEffect(m2.Owner, T.GotChasedBy_V1.F(m1.LinkToShortName), desc, wasGood);

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
                    }
                }
            }
        }

        private void CreateMentalEffect(BeingData b1, string text, string desc, bool wasGood) {
            var icon = IconId.CRunning;
            var str = wasGood ? 2 : -5;

            b1.Being.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(b1.Being.PosIdx,
                icon, G.Ticks, Consts.TicksPer3Hours, text, str).WithTooltip(desc));
        }

        private void Notify(StoryContext ctx, StoryEventData data, string outcome) {
            BuildLogEntry(data, outcome);
            ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
            // data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
            //     : Priority.Low,
            //     showPopup: false, showFanfare: false, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data, string outcome) {
            var minds = data.Participants;
            var desc = T.ChasingDesc_V2.F(
                minds[0].LinkToShortName, minds[1].LinkToShortName);

            data.CreateLogEntry(IconId.CRunning, desc, outcome);
        }
    }
}
