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
    public sealed class StabbingStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new StabbingStoryEvent());
        }

        public const string EventId = "Stabbing";
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
        private bool HasMotive(BeingData being) {
            if (being.Being == null) { return false; }
            if (HasForceAssignedGoal(being)) { return false; }
            if (being.Mind.Health == null) { return false; }
            if (being.Mind.Bonds == null) { return false; }
            if (being.Mind.Bonds.Opinions.Count == 0) { return false; }
            if (being.Mind.Health.Sanity.Value > 0.85f) {
                if (!being.Mind.Traits.HasAnyTrait(TraitIdH.Psychopath, TraitIdH.Sociopath, TraitIdH.Paranoid)) {
                    return false;
                }
            }
            if (!being.Mind.Species.IsHumanoid) { return false; }
            if (!being.HasAbility(AbilityIdH.Manipulate)) { return false; }
            if (being.Mind.Traits.HasTrait(TraitIdH.Pacifist)) { return false; }
            if (being.Mind.Bonds.Important.TryGetValue(BeingOpinionFlags.Nemesis, out var nemeses)) {
                for (int i = nemeses.Length - 1; i >= 0; i--) {
                    if (IsValidNemesis(being, nemeses[i])) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsValidNemesis(BeingData being, Mind nemesis) {
            if (nemesis.Health == null) { return false; }
            if (nemesis.Owner == null) { return false; }
            if (nemesis.Owner.Being == null) { return false; }
            if (HasForceAssignedGoal(nemesis.Owner)) { return false; }
            if ((nemesis.Owner.StateFlags & BeingState.IncapableOfCombat) != BeingState.Uninitialized) {
                return false;
            }
            if (nemesis.Owner.Body.IsIncapacitated) { return false; }
            if (nemesis.Owner.Being.AIAgent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            if (!being.Safety.IsActuallySafeAt(nemesis.Owner.Being.PosIdx)) {
                return false;
            }
            if (being.Being.Motion.IsGrounded
                    && !A.S.Sys.Areas.CanReachGrounded(being.Being.PosIdx, nemesis.Owner.Being.PosIdx)) {
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

            var nemeses = Caches.BeingDataList;
            nemeses.Clear();
            BeingData initiator = null;
            for (int i = cluster.FriendlySociable.Count - 1; i >= 0; i--) {
                var being = cluster.FriendlySociable[i];
                if (HasMotive(being)) {
                    if (being.Mind.Bonds.Important.TryGetValue(BeingOpinionFlags.Nemesis, out var nems)) {
                        for (int j = 0; j < nems.Length; j++) {
                            var nem = nems[j];
                            if (IsValidNemesis(being, nem)) {
                                nemeses.Add(nem.Owner);
                            }
                        }
                    }
                }
                if (nemeses.Count > 0) {
                    initiator = being;
                    break;
                }
            }

            if (initiator == null) {
                data.FailEvent(s, "Couldn't pick initiator for stabbing", isHardFailure: false);
                return;
            }
            if (nemeses.Count == 0) {
                data.FailEvent(s, "Couldn't pick nemesis for stabbing", isHardFailure: false);
                return;
            }
            var target = rng.From(nemeses);

            data.TargetBeing = initiator;
            data.AddParticipant(initiator);
            data.AddParticipant(target);
            if (BeginStabbing(ctx, data)) {
                s.Sys.Log.AddLine(initiator, $"{T.NounStalking}: {Texts.LinkToBeing(target)}");
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start a stabbing");
                }
            }
        }

        private bool BeginStabbing(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CStalker,
                    T.ActStalkingSomeone_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithFlag(AIGoalFlags.IgnoreDeadBodies)
                .WithWorldToDesired(AIVarsH.Stabbed, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Err("Failed assigning stabbing goal (lead) to: {0}", m1);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);
            data.TempObj = g1;

            return true;
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            data.TempObj ??= data.Participants[0].Owner.Being?.AIAgent.Goal;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var mind = Arrays.SafeGetFast(data.Participants, 0);
            var curAct = mind?.Owner?.Being?.AIAgent.CurrentAction;
            if (curAct?.IdH != AIActionStab.ActIdH) {
                var goal = data.TempObj as AIGoal;
                if (AIGoal.IsCompleted(goal)) {
                    data.SetState(ctx.S, StoryEventState.Ending);
                } else {
                    data.FailEvent(ctx.S, "Stabbing goal did not complete", isHardFailure: false);
                }
            }
        }

        private string StabOutcomeKey(Rng rng) {
            switch (rng.Range(0, 4)) {
                case 0: return T.ActStabbedKitchenKnife_V2;
                case 1: return T.ActStabbedFork_V2;
                case 2: return T.ActStabbedScissors_V2;
                case 3: return T.ActStabbedPencil_V2;
            }
            D.Err("Unhandled range in stab outcome key");
            return T.ActStabbedKitchenKnife_V2;
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            // someone left the ship before it concluded (a dead victim keeps the corpse's Being)
            if (!data.HasParticipantsAboard(2)) { return; }
            var s = ctx.S;
            var rng = data.SeededRng;

            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var attitudeChangeAmt = -0.25f;
            var hateChange = 0.25f;
            var friendshipChange = -0.25f;
            string desc = StabOutcomeKey(rng).F(m1.LinkToShortName, m2.LinkToShortName);

            CreateMentalEffect(m1.Owner, T.StabbedSomeone_V1.F(m2.LinkToShortName), desc, true);
            if (m2.Owner.HasStateFlag(BeingState.Alive)) {
                CreateMentalEffect(m2.Owner, T.StabbedBySomeone_V1.F(m1.LinkToShortName), desc, false);
            } else {
                m2.OnDeath(desc, G.Ticks);
            }

            Notify(ctx, data, desc);

            var soc = SocialInteraction.For(data.LogEntry);
            soc.WithAttitude(attitudeChangeAmt)
                .WithFriendship(friendshipChange)
                .WithHate(hateChange)
                .With(m1.Owner, 0.01f)
                .With(m2.Owner, 1f);

            ctx.S.Query.RegisterSocialInteraction.Ask(soc);

            if (m2.Owner.HasStateFlag(BeingState.Alive) && !m2.Owner.Body.IsIncapacitated) {
                var op2 = s.Query.GetOpinionAbout.Ask(m2, m1);
                var chanceRetaliate = (-op2.SocialAffinity * 0.25f) + op2.Hate * 0.5f;
                chanceRetaliate = Mathf.Lerp(0.05f, 0.90f, chanceRetaliate);
                if (rng.Chance(chanceRetaliate)) {
                    if (m2.Jobs.CanDo(JobTypeIdH.Melee)) {
                        ctx.S.Query.StartFriendlyFightBetween.Ask(
                            m2.Owner.Being.AIAgent.CombatUnit,
                            m1.Owner.Being.AIAgent.CombatUnit);
                    }
                }
            }
        }

        private void CreateMentalEffect(BeingData b1, string text, string desc, bool wasGood) {
            if (b1 == null || !b1.HasStateFlag(BeingState.Alive)) {
                return;
            }
            var icon = IconId.CKnife;
            var str = wasGood ? 15 : -15;
            b1.Being?.AIAgent.AddMentalEffect(MentalEffect.CreateWithIcon(b1.Being.PosIdx,
                icon, G.Ticks, Consts.TicksPer2Days, text, str).WithTooltip(desc));
        }

        private void Notify(StoryContext ctx, StoryEventData data, string outcome) {
            BuildLogEntry(data, outcome);
            data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
                showPopup: true, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data, string outcome) {
            data.CreateLogEntry(IconId.CKnife, T.NounStabbing, outcome);
        }
    }
}
