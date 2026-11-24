using KL.Randomness;
using UnityEngine;
using Game.CodeGen;
using Game.Data;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Drama;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class FriendlyFightStoryEvent : StoryEvent {
        public const string EventId = "FriendlyFight";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new FriendlyFightStoryEvent());
        }

        private static bool CheckBiologicals(GameState state) {
            int numBiologicals = 0;
            int numUnhappy = 0;
            foreach (var bd in state.Query.AllBeingData.Ask().Values) {
                if (!bd.IsFriendly) { continue; }
                if (bd.Being == null) { continue; }
                if (HasForceAssignedGoal(bd)) { continue; }
                if (!bd.Mind.Species.IsBiological) {
                    continue;
                }
                numBiologicals++;
                if (numBiologicals > 1 && numUnhappy > 0) {
                    return true;
                }
                if (bd.Mind.Health == null) { continue; }
                if (bd.Being.AIAgent.Goal?.IsNonInterruptable == true) { continue; }
                if (bd.StatsBeing.Sanity.Value < 0.5f) {
                    numUnhappy++;
                    if (numBiologicals > 1 && numUnhappy > 0) {
                        return true;
                    }
                }
            }
            return numBiologicals > 1 && numUnhappy > 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var isSandbox = A.InstantBuild;
            #if UNITY_EDITOR
            isSandbox = true;
            #endif
            if (isSandbox && ctx.S.Query.SelectBeingCount.Ask() == 2) {
                // don't check biologicals, and immediately begin
                OnTick(ctx, data);
                return;
            }
            if (!CheckBiologicals(ctx.S)) {
                data.FailEvent(ctx.S, "Can't start friendly fight", isHardFailure: false);
            }
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return CheckBiologicals(ctx.S);
        }

        private readonly Dictionary<(Being, Being), float> candidates = new();

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var nextAttempt = data.Temp.GetLong("NextAttempt", 0L);
            if (nextAttempt > 0 && ctx.Ticks > nextAttempt) {
                return;
            }
            var s = ctx.S;
            var clusters = s.Query.GetSociableClusters.Ask();
            candidates.Clear();
            var rng = data.SeededRng;

            var startWithSelected = A.InstantBuild;
            #if UNITY_EDITOR
            startWithSelected = true;
            #endif
            if (startWithSelected) {
                var selected = s.Query.GetSelectedBeings.Ask();
                if (selected.Count == 2 && CanFight(selected[0], selected[1])) {
                    candidates.Add((selected[0], selected[1]), 1f);
                }
            }

            if (candidates.Count == 0) {
                FillCandidates(clusters, rng);
            }

            if (!rng.TryWeightedFrom(candidates, out var pair)) {
                if (ctx.Ticks - data.StartTick > Consts.TicksPerWeek) {
                    // failed to start friendly fight for a week, ending event
                    data.FailEvent(ctx.S, "Could not find candidates for a week",
                        isHardFailure: false);
                    return;
                }
                // will try on next tick
                data.Temp.SetLong("NextAttempt", ctx.Ticks + Consts.TicksPer5Minutes);
                return;
            }
            // A fight that never started (a raid broke out meanwhile) costs nobody an opinion
            if (!StartFightBetween(ctx, pair.Item1, pair.Item2, out var attacker, out var prey)) {
                data.Temp.SetLong("NextAttempt", ctx.Ticks + Consts.TicksPer5Minutes);
                return;
            }
            data.HideLogEntry();
            data.TargetBeing = attacker.Data;
            data.AddParticipant(attacker.Data);
            data.AddParticipant(prey.Data);
            OnFightStarted(ctx.S, attacker, prey, false);
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        // What a friendly fight does to the relationship. Shared with the "pick a
        // fight" mental breakdown, which starts the same kind of fight.
        public static void RegisterFightInteraction(GameState s, Being b1, Being b2) {
            RegisterFightInteraction(s, b1, b2, null);
        }

        private static void RegisterFightInteraction(GameState s, Being b1, Being b2,
                string cause) {
            var desc = T.FightBetween_V2.F(
                b1.Data.Mind.LinkToShortName,
                b2.Data.Mind.LinkToShortName);
            if (cause != null) {
                desc = $"{desc}. {cause}";
            }
            var soc = SocialInteraction.Create(IconId.CFist, T.FightOngoing, desc)
                .WithAttitude(-0.2f)
                .With(b1.Data, 0.1f)
                .With(b2.Data, 1f);
            s.Query.RegisterSocialInteraction.Ask(soc);
        }

        // A fistfight says why it started: the start line alone told the player nothing
        // about the two people (Steam review, 2026-11). Logged after the start line.
        public static void OnFightStarted(GameState s, Being attacker, Being opponent,
                bool fromBreakdown) {
            var cause = DescribeCause(attacker, opponent, fromBreakdown);
            s.Sys.Log.AddLine(cause);
            RegisterFightInteraction(s, attacker, opponent, cause);
        }

        // A hate the Social tab calls Moderate or stronger (Texts.RelationshipStrength)
        private const float GrudgeHate = 0.35f;
        // The event's own bar for an unhappy being (CheckBiologicals)
        private const float BadMoodSanity = 0.5f;

        // First match wins. The pair was picked by hate, low sanity and a hidden negative
        // affinity (FillCandidates); the affinity's number stays hidden, so it is said in words.
        public static string DescribeCause(Being attacker, Being opponent, bool fromBreakdown) {
            var a = attacker.Data.Mind.LinkToShortName;
            var b = opponent.Data.Mind.LinkToShortName;
            if (HateOf(attacker, opponent) >= GrudgeHate) {
                return T.FightCauseGrudge_V2.F(a, b);
            }
            if (fromBreakdown) {
                // the breakdown goes for the most hated or else the nearest person
                return T.FightCauseNearest_V2.F(a, b);
            }
            if (attacker.Data.StatsBeing.Sanity.Value < BadMoodSanity) {
                var line = T.FightCauseMood_V1.F(a);
                var worst = WorstMoodOf(attacker.Data);
                return worst == null ? line : $"{line} ({worst})";
            }
            return T.FightCauseClash_V2.F(a, b);
        }

        private static float HateOf(Being being, Being other) {
            var bonds = being.Data.Mind.Bonds;
            if (bonds == null
                    || !bonds.Opinions.TryGetValue(other.Data.Mind.Id, out var op)) {
                return 0f;
            }
            return op.Hate;
        }

        private static string WorstMoodOf(BeingData data) {
            var effects = data.Mind.Health?.CurrentEffects;
            if (effects == null) { return null; }
            string worst = null;
            int worstValue = 0;
            for (int i = 0; i < effects.Count; i++) {
                var e = effects[i];
                if (e.Effect < worstValue && e.Reason != null) {
                    worst = e.Reason;
                    worstValue = e.Effect;
                }
            }
            return worst;
        }

        private void FillCandidates(List<BeingCluster> clusters, Rng rng) {
            foreach (var cluster in clusters) {
                if (cluster.IsExpedition) { continue; }
                foreach (var data in cluster.FriendlySociable) {
                    if (data.Being == null) { continue; }
                    if (data.Mind.Bonds == null) { continue; }
                    float sanity = data.StatsBeing.Sanity.Value;
                    foreach (var op in data.Mind.Bonds.Opinions.Values) {
                        if (sanity < 0.25f || op.Hate > 0.05f ||
                                (op.SocialAffinity < 0f && rng.Chance(0.10f))) {
                            var w = op.Hate * 2f - op.SocialAffinity * 0.5f;
                            w += 1f - sanity;
                            if (w <= 0f) { w = 0.01f; }
                            var target = op.TargetMind;
                            if (target.Owner?.Being == null) { continue; }
                            if (cluster.FriendlySociable.Contains(target.Owner)) {
                                if (CanFight(data.Being, target.Owner.Being)) {
                                    if (data.Species.IsAnimal != target.Owner.Species.IsAnimal) {
                                        // reduce chance of non-animal vs animal friendly fights
                                        if (rng.Chance(0.9f)) {
                                            continue;
                                        }
                                    }
                                    candidates.Add((data.Being, target.Owner.Being), w);
                                }
                            }
                        }
                    }
                }
            }
        }

        private bool StartFightBetween(StoryContext ctx, Being b1, Being b2,
                out Being attacker, out Being prey) {
            attacker = b1;
            prey = b2;
            if (b2.Data.StatsBeing.Sanity.Value < b1.Data.StatsBeing.Sanity.Value) {
                attacker = b2;
                prey = b1;
            }
            return ctx.S.Query.StartFriendlyFightBetween.Ask(
                attacker.AIAgent.CombatUnit, prey.AIAgent.CombatUnit);
        }

        public static bool CanFight(Being b1, Being b2) {
            return IsFitToFight(b1, false) && IsFitToFight(b2, false);
        }

        // Single rule set for who can end up in a friendly fight, shared with the
        // "pick a fight" mental breakdown so both paths agree.
        // ignoreOwnGoal: the breakdown is a force assigned goal itself, and it ends
        // right before the fight starts, so it must not disqualify its own being.
        public static bool IsFitToFight(Being b, bool ignoreOwnGoal) {
            if (!b.Data.IsFriendly) { return false; }
            if (!ignoreOwnGoal && HasForceAssignedGoal(b.Data)) { return false; }
            if (b.Data.Mind.Health == null) { return false; }
            if (b.Carrier != null) { return false; }
            if (!b.CanFight) { return false; }
            if (!b.Data.Mind.Jobs.CanDo(JobTypeIdH.Melee)) { return false; }
            if (b.Data.Mind.Traits.HasTrait(TraitIdH.Pacifist)) { return false; }
            if (b.Data.HasCondition(ConditionIdH.Sleeping)) { return false; }
            if (b.AIAgent.DirectControl.IsUnderDirectControl) { return false; }
            return true;
        }
    }
}
