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
    public sealed class MournStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new MournStoryEvent());
        }

        protected override void Cleanup() {
            wMourners.Clear();
        }

        public const string EventId = "Mourn";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return !ctx.S.Sys.Combat.IsHostileCombatInProgress;
        }

        public override bool CheckHook(StoryContext ctx, StoryEventDef def, StoryEventHook hook) {
            if (hook.Target is not List<BeingCluster> clusters) {
                D.Warn("Hook target not list of being clusters! {0}", hook.Target);
                return false;
            }
            var alive = ctx.S.Beings.Alive;
            var s = ctx.S;
            for (int i = alive.Count - 1; i >= 0; i--) {
                var b = alive[i];
                if (HasMotive(s, b.Data)) {
                    return true;
                }
            }
            return false;
        }

        private bool HasMotive(GameState s, BeingData being) {
            if (being.Being == null) { return false; }
            if (HasForceAssignedGoal(being)) { return false; }
            if (!being.IsPlayer) { return false; }
            if (being.Mind.Bonds?.Opinions == null) { return false; }
            if (!being.Mind.Jobs.CanDo(JobTypeIdH.Social)) { return false; }
            if (being.Mind.Traits.HasTrait(TraitIdH.Enlightened)) { return false; }
            foreach (var op in being.Mind.Bonds.Opinions.Values) {
                if (IsMournable(s, op, out _, out _)) {
                    return true;
                }
            }
            return false;
        }

        private bool IsMournable(GameState s, BeingOpinion op, out Mind mind, out float weight) {
            if ((op.Flags & BeingOpinionFlags.Mournable) == BeingOpinionFlags.None) {
                weight = 0f;
                mind = null;
                return false;
            }
            mind = op.TargetMind;
            if (mind.Owner != null) {
                if (mind.Owner.HasStateFlag(BeingState.Alive)) {
                    weight = 0f;
                    return false;
                }
            }

            if (op.NextMournTick > G.Ticks) {
                weight = 0f;
                return false;
            }

            weight = op.Friendship + op.Love + op.Pet - op.Hate;
            if (weight <= 0.01f) {
                weight = 0.01f;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var rng = data.SeededRng;
            PickParticipants(ctx, rng, out var mourner, out var target);

            if (mourner == null) {
                data.FailEvent(s, "Couldn't pick initiator for mourning", isHardFailure: false);
                return;
            }
            if (target == null) {
                data.FailEvent(s, "Couldn't pick target for mourning", isHardFailure: false);
                return;
            }

            data.TargetBeing = mourner;
            data.AddParticipant(mourner);
            data.AddParticipant(target);
            if (BeginMourning(ctx, data, mourner, target)) {
                data.SetState(s, StoryEventState.Ongoing);
            } else {
                if (!data.IsFailed) {
                    data.FailEvent(s, "Failed to start stalking");
                }
            }
        }

        private readonly Dictionary<(BeingData, Mind), float> wMourners = new();

        private bool PickParticipants(StoryContext ctx, Rng rng,
                out BeingData mourner, out Mind target) {
            var s = ctx.S;
            wMourners.Clear();
            foreach (var b in s.Beings.Alive) {
                if (!HasMotive(s, b.Data)) { continue; }
                foreach (var op in b.Data.Mind.Bonds.Opinions.Values) {
                    if (IsMournable(s, op, out var mind, out float w)) {
                        wMourners.Add((b.Data, mind), w);
                    }
                }
            }
            if (rng.TryWeightedFrom(wMourners, out var res)) {
                mourner = res.Item1;
                target = res.Item2;
                return true;
            }
            mourner = null;
            target = null;
            return false;
        }

        private bool BeginMourning(StoryContext ctx, StoryEventData data,
                BeingData mourner, Mind target) {
            var s = ctx.S;

            var g1 = AIGoal.DirectOrder(JobTypeIdH.Social, IconId.CFuneralRibbon,
                    T.ActMourningSomeone_V1.F(target.LinkToShortName),
                    mourner.Being.AIAgent, ctx.Ticks)
                .WithVar(AIGoalVars.MindId_Uint, target.Id)
                .WithFlag(AIGoalFlags.IgnoreDeadBodies)
                .WithWorldToDesired(AIVarsH.Mourned, false, true);
            data.TempObj = g1;
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Err("Failed assigning mourning goal to: {0}", mourner);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);

            return true;
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            data.TempObj ??= Arrays.SafeGetFast(data.Participants, 0)?.Owner?.Being?.AIAgent.Goal;
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var mourner = Arrays.SafeGetFast(data.Participants, 0)?.Owner?.Being;
            if (mourner == null) {
                // the mourner left the ship
                D.Warn("Mourner is null! (Being missing): {0}", data);
                data.SetState(ctx.S, StoryEventState.Ending);
                return;
            }
            // deferred loading
            if (data.TempObj == null) {
                var goal = mourner.AIAgent.Goal;
                if (goal == null || goal.JobIdH != JobTypeIdH.Social) {
                    D.Err("Null or wrong goal when restoring mourn story event on tick: {0}", goal);
                }
            }
            if (mourner.AIAgent.CurrentAction?.IdH != AIActionMourn.ActIdH) {
                data.SetState(ctx.S, StoryEventState.Ending);
            }
        }

        private Curve mournDelayCurve;

        private void DelayNextMourn(GameState s, Mind m1, Mind m2, long ticks) {
            var op = s.Query.GetOpinionAbout.Ask(m1, m2);
            var deadMonths = (ticks - m2.LastTimeOfDeath) / (float) Consts.TicksPerMonth;
            mournDelayCurve ??= CurveDef.FastGet(CurveIdH.Drama_MournWeeksDelayByDeathMonths);
            var mournDelayWeeks = mournDelayCurve.Evaluate(deadMonths);
            op.NextMournTick = ticks + (int) (mournDelayWeeks * Consts.TicksPerWeek);
            D.Warn("{0} mourned {1} who was dead for {2} months (death time: {3}, time now: {4}). Mourn delay: {5} weeks",
                m1, m2, deadMonths, Units.TicksToTime(m2.LastTimeOfDeath),
                    Units.TicksToTime(ticks), mournDelayWeeks);
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var m1 = data.Participants[0];
            var m2 = data.Participants[1];

            var success = false;
            if (data.TempObj is AIGoal goal) {
                success = AIGoal.IsCompleted(goal);
            }

            var title = T.EvMournedTitle_V2.F(
                m1.LinkToShortName,
                m2.LinkToShortName);
            string desc = success ? T.EvMournedOkDesc_V2 : T.EvMournedFailDesc_V2;
            desc = desc.F(m1.LinkToShortName, m2.LinkToShortName);
            data.CreateLogEntry(IconId.CFuneralRibbon, title, desc);
            var soc = SocialInteraction.For(data.LogEntry)
                .WithAttitude(0.01f)
                .With(m1, 1f)
                .With(m2, 0f);
            s.Query.RegisterSocialInteraction.Ask(soc);
            Notify(ctx, data, success);

            DelayNextMourn(s, m1, m2, ctx.Ticks);
        }

        private void Notify(StoryContext ctx, StoryEventData data, bool success) {
            ctx.S.Sys.Log.AddLine(data.LogEntry.Desc);
        }
    }
}
