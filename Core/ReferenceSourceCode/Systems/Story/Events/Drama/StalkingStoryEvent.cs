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
    public sealed class StalkingStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new StalkingStoryEvent());
        }

        public const string EventId = "Stalking";
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

        private bool HasMotive(BeingData being) {
            if (being.Being == null) { return false; }
            if (HasForceAssignedGoal(being)) { return false; }
            if (being.Mind.Health == null) { return false; }
            if (being.Mind.Bonds == null) { return false; }
            if (being.Mind.Bonds.Opinions.Count == 0) { return false; }
            if (!being.IsFriendly) { return false; }
            if (being.Mind.Traits.HasTrait(TraitIdH.Psychopath)) { return true; }
            if (being.Mind.Traits.HasTrait(TraitIdH.Stalker)) { return true; }
            if (being.Mind.Traits.HasTrait(TraitIdH.Sociopath)) { return true; }
            if (being.Mind.Traits.HasTrait(TraitIdH.Curious)) { return true; }
            foreach (var op in being.Mind.Bonds.Opinions.Values) {
                if ((op.Flags & BeingOpinionFlags.StalkerTarget) == BeingOpinionFlags.None) {
                    continue;
                }
                if (IsValidTarget(being, op.TargetMind)) {
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
            if (data.TargetOther is not List<BeingCluster> clusters) {
                data.FailEvent(s,
                    $"TargetOther is not a list of being clusters: {data.TargetOther}");
                return;
            }
            var rng = data.SeededRng;
            var cluster = clusters.Where(cl => IsClusterCompatible(ctx, cl))
                .OrderBy(cl => rng.Value).FirstOrDefault();

            PickParticipants(ctx, rng, cluster, out var stalker, out var target);

            if (stalker == null) {
                data.FailEvent(s, "Couldn't pick initiator for stalking", isHardFailure: false);
                return;
            }
            if (target == null) {
                data.FailEvent(s, "Couldn't pick prey for stalking", isHardFailure: false);
                return;
            }

            data.TargetBeing = stalker;
            data.AddParticipant(stalker);
            data.AddParticipant(target);
            if (BeginStalking(ctx, data)) {
                s.Sys.Log.AddLine(stalker, $"{T.NounStalking}: {Texts.LinkToBeing(target)}");
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start stalking", isHardFailure: false);
                }
            }
        }

        private bool PickParticipants(StoryContext ctx, Rng rng, BeingCluster cluster,
                out BeingData stalker, out BeingData target) {
            var wPrey = Caches.WeightedBeingData;
            wPrey.Clear();
            stalker = null;
            List<BeingData> candidates;
            if (cluster != null) {
                candidates = cluster.FriendlySociable;
            } else {
                ///
                candidates = Caches.BeingDataList;
                candidates.Clear();
                foreach (var b in ctx.S.Beings.Alive) {
                    if (b.AIAgent.CanWork && HasMotive(b.Data)) {
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
                    stalker = being;
                    break;
                }
            }
            if (stalker == null) {
                D.Warn("Couldn't pick stalker from beings: {0}", D.DumpCollection(candidates));
                target = null;
                return false;
            }
            if (wPrey.Count == 0) {
                // fallback
                foreach (var op in stalker.Mind.Bonds.Opinions.Values) {
                    var w = op.Hate + op.Love + 0.01f;
                    var mind = ctx.S.Query.FindMind.Ask(op.TargetMindId);
                    if (!IsValidTarget(stalker, mind)) { continue; }
                    wPrey.Add(mind.Owner, w);
                }
            }
            if (wPrey.Count == 0) {
                // final fallback
                foreach (var being in ctx.S.Beings.Alive) {
                    if (HasForceAssignedGoal(being.Data)) { continue; }
                    if (!IsValidTarget(stalker, being.Data.Mind)) {
                        wPrey.Add(being.Data, 1f);
                        continue;
                    }
                }
            }
            return rng.TryWeightedFrom(wPrey, out target);
        }

        private bool TryEnqueuePrey(StoryContext ctx, Dictionary<BeingData, float> wPrey, BeingData being) {
            if (!HasMotive(being)) { return false; }
            foreach (var op in being.Mind.Bonds.Opinions.Values) {
                var mind = ctx.S.Query.FindMind.Ask(op.TargetMindId);
                if (IsValidTarget(being, mind)) {
                    wPrey.Add(mind.Owner, op.Hate + op.Love);
                }
            }

            return true;
        }

        private bool BeginStalking(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CStalker,
                    T.ActStalkingSomeone_V1.F(m2.LinkToShortName),
                    m1.Owner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.Stalked, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Warn("Failed assigning stalking goal to: {0} | {1}",
                    m1, m1.Owner.Being.AIAgent);
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
            if (curAct?.IdH != AIActionStalk.ActIdH) {
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

            var wasFoundOut = rng.Chance(0.5f);
            var likedBeingFoundOut = false;
            var op2 = s.Query.GetOpinionAbout.Ask(m2, m1);
            #if !PRODUCTION_BUILD
            if (s.BB.Get(BBKeyH.DevStalkForceCaught, false)) {
                wasFoundOut = true;
            }
            #endif
            if (wasFoundOut) {
                // var op1 = s.Query.GetOpinionAbout.Ask(m1, m2);
                var chanceDidntHate = op2.SocialAffinity * 0.25f
                    + op2.SexualAttraction * 0.25f
                    + op2.Love * 0.25f
                    - op2.Hate;
                chanceDidntHate = Mathf.Lerp(0.15f, 0.90f, chanceDidntHate);
                // negative mental boost for stalker
                if (rng.Chance(chanceDidntHate)) {
                    // positive mental boost for prey
                    likedBeingFoundOut = true;
                }
            }

            string desc;
            if (wasFoundOut) {
                float attitudeChange;
                float loveChange;
                float hateChange;
                if (likedBeingFoundOut) {
                    desc = T.StalkingOutcomeCaughtLiked_V2;
                    attitudeChange = 0.02f;
                    hateChange = 0f;
                    // if prey had sexual attraction to stalker, let's increase love
                    loveChange = op2.SexualAttraction * 0.03f;

                } else {
                    desc = T.StalkingOutcomeCaughtHated_V2;
                    attitudeChange = -0.05f;
                    hateChange = 0.03f;
                    loveChange = -0.05f;
                }
                desc = desc.F(m1.LinkToShortName, m2.LinkToShortName);
                data.CreateLogEntry(IconId.CStalker, T.StalkingTitle_V2.F(
                    m1.LinkToShortName, m2.LinkToShortName), desc);
                var soc = SocialInteraction.For(data.LogEntry)
                    .WithAttitude(attitudeChange)
                    .WithLove(loveChange)
                    .WithHate(hateChange)
                    .With(m1.Owner, 0f)
                    .With(m2.Owner, 1f);
                ctx.S.Query.RegisterSocialInteraction.Ask(soc);
                if (likedBeingFoundOut) {
                    CreateMentalEffect(m2.Owner, T.StalkingCaughtStalker, desc, 3);
                } else {
                    CreateMentalEffect(m2.Owner, T.StalkingCaughtStalker, desc, -7);
                }
                CreateMentalEffect(m1.Owner, T.StalkingGotCaught, desc, -10);
                Notify(ctx, data);
            } else {
                data.HideLogEntry();
                desc = T.StalkingOutcomeNotCaught_V2.F(m1.LinkToShortName, m2.LinkToShortName);
                CreateMentalEffect(m1.Owner, T.StalkedSomeone_V1.F(m2.LinkToShortName), desc, 5);
            }

            if (wasFoundOut && !likedBeingFoundOut) {
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

        private void CreateMentalEffect(BeingData b1, string text, string desc, short change) {
            var icon = IconId.CStalker;
            b1.Mind.Health?.AddEffect(A.S, MentalEffect.CreateWithIcon(
                b1.Being?.PosIdx ?? b1.LastPosition.Idx,
                icon, G.Ticks, Consts.TicksPer8Hours, text, change)
                    .WithTooltip(desc));
        }

        private void Notify(StoryContext ctx, StoryEventData data) {
            ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
            // data.AddEventNotification(ctx, data.LogEntry.Icon, data.LogEntry.Title,
            //     priority: Priority.Low,
            //     showPopup: false, showFanfare: false, addLogLine: true);
        }
    }
}
