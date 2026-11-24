using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;

namespace Game.Systems.Story {
    public sealed class InspiredStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new InspiredStoryEvent());
        }

        public const string EventId = "Inspired";
        public override string Id => EventId;

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            Caches.WeightedBeingData.Clear();
            var allBeings = s.Query.AllBeingData.Ask();
            var rng = data.SeededRng;
            foreach (var being in allBeings.Values) {
                if (!CanBeInspired(being)) {
                    continue;
                }
                Caches.WeightedBeingData.Add(being, rng.Value + 0.1f);
            }

            if (!rng.TryWeightedFrom(Caches.WeightedBeingData, out var victim)) {
                data.FailEvent(s, "No eligible beings found to inspire", isHardFailure: false);
                return;
            }

            Caches.WeightedBeingData.Clear();

            Severity sev = rng.OneOf(Severity.Moderate, Severity.Critical, Severity.Severe);

            if (ConditionState.CreateFor(s, ConditionIdH.Inspired, victim, sev, true)) {
                // progress will go down
                ConditionState.SetProgress(victim, ConditionIdH.Inspired, 0.99f);
                data.TargetBeing = victim;
                Notify(ctx, data, victim);
                data.SetState(s, StoryEventState.Ending);
            } else {
                D.Err("Could not create inspired condition for being: {0}", victim);
                data.FailEvent(s, "Could not create inspired condition");
            }
        }

        private void Notify(StoryContext ctx, StoryEventData data, BeingData victim) {
            BuildLogEntry(data, victim);
            data.TargetBeing = victim;
            data.AddEventNotification(ctx, IconId.CUpgrade, T.CondInspired,
                showPopup: false, showFanfare: true, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data, BeingData victim) {
            data.CreateLogEntry(IconId.CUpgrade,
                T.CondInspired, T.EvInspiredDesc_V1.F(victim.Mind.LinkToShortName));
        }

        private static bool CanBeInspired(BeingData being) {
            if (being.HasStateFlag(BeingState.Dead)) { return false; }
            if (being.Body.HasFlag(BodyFlags.Missing)) { return false; }
            if (!being.IsPlayer) { return false; }
            if (!being.Mind.Species.IsHumanoid) { return false; }
            if (being.HasCondition(ConditionIdH.Inspired)) { return false; }
            return true;
        }
    }
}
