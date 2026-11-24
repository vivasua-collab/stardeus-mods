using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.CodeGen;
using Game.Systems.Mental;
using UnityEngine;
using Game.Utils;

namespace Game.Systems.Story {
    public sealed class MutinyStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new MutinyStoryEvent());
        }

        public const string EventId = "Mutiny";
        public override string Id => EventId;

        private const int MinMutineers = 3;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (!A.IsCombatOn || ctx.S.Situation.Colonists == 0) {
                return false;
            }
            CountSides(ctx.S, out var disloyal, out var loyal);
            return disloyal >= MinMutineers && disloyal > loyal;
        }

        // Trust decides the mutiny: low sanity is the grievance, but only those who don't trust
        // the AI join, and a crew that trusts it at least as much as it doesn't stands by the
        // ship, so nobody starts one.
        private static void CountSides(GameState s, out int disloyal, out int loyal) {
            disloyal = 0;
            loyal = 0;
            foreach (var being in s.Beings.Alive) {
                if (CanMutiny(being)) {
                    disloyal++;
                } else if (IsLoyal(being)) {
                    loyal++;
                }
            }
        }

        // People only: robots have no say in it. Chipped colonists count: they won't act
        // against the ship, which is all it takes to stand by it.
        private static bool IsLoyal(Being being) {
            var data = being.Data;
            if (!data.Mind.Species.IsHumanoid || data.Mind.Health == null) { return false; }
            if (data.Body.IsIncapacitated) { return false; }
            if (!data.EffectiveFaction.IsPlayer) { return false; }
            return data.Body.IsForcedToObey || data.StatsBeing.AIAlignment.Value > Trust.MutinyLine;
        }

        private static bool CanMutiny(Being being) {
            if (!being.Data.Mind.Species.IsHumanoid) { return false; }
            if (being.Data.StatsBeing.AIAlignment.Value > Trust.MutinyLine) { return false; }
            if (being.Data.StatsBeing.Sanity.Value > 0.8f) { return false; }
            if (being.Data.Body.IsIncapacitated) { return false; }
            if (!being.Data.EffectiveFaction.IsPlayer) { return false; }
            if (being.Data.Mind.Traits.HasAnyTrait(
                    TraitIdH.Pacifist, TraitIdH.Enlightened)) {
                return false;
            }
            if (being.AIAgent.Goal?.IsNonInterruptable == true) { return false; }
            if (HasForceAssignedGoal(being.Data)) { return false; }
            if (being.Data.Body.IsForcedToObey) { return false; }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            CountSides(s, out var disloyal, out var loyal);
            if (disloyal <= loyal) {
                data.FailEvent(s, "The crew trusts the AI too much for a mutiny",
                    isHardFailure: false);
                return;
            }
            var weightedBeings = new Dictionary<Being, float>();
            foreach (var being in s.Beings.Alive) {
                if (!CanMutiny(being)) { continue; }
                // the less they trust the AI, the likelier they lead it
                var trust = Mathf.Clamp01(being.Data.StatsBeing.AIAlignment.Value);
                var weight = 1f - trust;
                if (being.Data.Mind.Traits.HasAnyTrait(TraitIdH.Psychopath, TraitIdH.Paranoid)) {
                    weight *= 1.1f;
                }
                if (weight > 0f) {
                    weightedBeings.Add(being, weight);
                }
            }
            if (data.TargetBeing?.Being != null
                    && !CanMutiny(data.TargetBeing.Being)) {
                data.FailEvent(s, "Target being is not eligible for mutiny",
                    isHardFailure: false);
                return;
            }
            if (weightedBeings.Count == 0 && data.TargetBeing == null) {
                data.FailEvent(s, "No eligible mutineers found", isHardFailure: false);
                return;
            }
            var mutinyFaction = FactionDef.FastGet(FactionIdH.AntiPlayer);
            var baseline = (float) MinMutineers;
            var wantedMutineers = baseline * s.Difficulty.Level;
            var maxMutineers = weightedBeings.Count;
            var rng = data.SeededRng;
            var mutinySize = Mathf.RoundToInt(wantedMutineers * rng.Range(0.9f, 1.1f));
            if (mutinySize > maxMutineers) {
                mutinySize = maxMutineers;
            } else if (mutinySize < 1) {
                mutinySize = 1;
            }
            var mutineers = new List<Being>();
            if (data.TargetBeing != null && data.TargetBeing.Being != null) {
                mutineers.Add(data.TargetBeing.Being);
            }
            if (mutinySize == maxMutineers) {
                mutineers.AddRange(weightedBeings.Keys);
            } else {
                for (int i = 0; i < mutinySize; i++) {
                    if (rng.TryWeightedFrom(weightedBeings, out var mutineer)) {
                        mutineers.Add(mutineer);
                        weightedBeings.Remove(mutineer);
                    }
                }
            }
            if (mutineers.Count == 0) {
                data.FailEvent(s, "No mutineers selected");
                return;
            }
            foreach (var mutineer in mutineers) {
                data.TargetBeing ??= mutineer.Data;
                data.AddParticipant(mutineer.Data);
                s.Sys.Log.AddLine(mutineer, Texts.Red(T.JoinedMutiny));
            }
            new CmdRestrictClock(ClockSpeed.Normal).Execute(s);
            s.Sys.Combat.StartRaidBy(mutinyFaction, mutineers);
            The.AchievementTracker.Unlock(Achievements.AchievementMutiny.AchievementId);
            data.SetState(s, StoryEventState.Ending);
            NotifyMutiny(ctx, data);
        }

        private void NotifyMutiny(StoryContext ctx, StoryEventData data) {
            BuildLogEntry(data);
            data.AddEventNotification(ctx, IconId.CKnife, T.EvtMutinyTitle);
        }

        private void BuildLogEntry(StoryEventData data) {
            var evDesc = T.EvtMutinyDesc;
            data.CreateLogEntry(IconId.CKnife, T.EvtMutinyTitle, evDesc);
        }
    }
}
