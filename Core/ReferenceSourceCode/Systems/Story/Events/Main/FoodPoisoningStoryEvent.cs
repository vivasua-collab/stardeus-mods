using Game.Data;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Conditions;
using Game.Systems.AI;

namespace Game.Systems.Story {
    public sealed class FoodPoisoningStoryEvent : StoryEvent {
        public const string EventId = "FoodPoisoning";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new FoodPoisoningStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Situation.Colonists > 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (ctx.S.Situation.Colonists < 1) {
                data.FailEvent(ctx.S, "No colonists to infect", isHardFailure: false);
            }
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (InfectOneColonist(ctx, data)) {
                data.SetState(ctx.S, StoryEventState.Ending);
                data.CreateLogEntry(IconId.CPoison, T.BreakdownBreakdownfoodpoisoning,
                    T.BreakdownBreakdownfoodpoisoningDesc_V1.F(
                        Texts.LinkToBeing(data.TargetBeing)));
                data.AddEventNotification(ctx, IconId.CPoison, T.BreakdownBreakdownfoodpoisoning,
                    showPopup: false, showFanfare: true, addLogLine: false);
            }
        }

        public static bool InfectOneColonist(StoryContext ctx, StoryEventData data) {
            // select a humanoid who is currently eating/drinking and not immune
            Caches.WeightedBeingData.Clear();
            var s = ctx.S;
            var beings = s.Query.AllBeingData.Ask();
            foreach (var being in beings.Values) {
                if (being.HasStateFlag(BeingState.Dead)) { continue; }
                // Stated rather than left to the eating-action check below, which only
                // happens to exclude sleepers: nobody in a pod or off the ship is a target.
                if (being.Body.HasFlag(BodyFlags.Missing)) { continue; }
                if (!being.Species.IsBiological) { continue; }
                if (being.HasCondition(ConditionIdH.FoodPoisoning)) { continue; }
                if (being.Conditions.TryGetValue(ConditionIdH.FoodPoisoning_Immunity, out var immunity)) {
                    if (immunity.Severity > Severity.Mild) {
                        continue;
                    }
                }
                // Only infect if currently eating/drinking
                var curActId = being.Being?.AIAgent?.CurrentAction?.IdH ?? 0;
                if (curActId == 0) { continue; }
                if (curActId != AIActionConsumeFood.ActIdH
                        && curActId != AIActionConsumeDrink.ActIdH
                        && curActId != AIActionConsumeAnimalFood.ActIdH) {
                    continue;
                }
                Curve infectCurve = Tunables.DiseaseTransmissionSpeedByAgeCurve;
                Caches.WeightedBeingData.Add(being, 1f + infectCurve.Evaluate(being.Body.AgePercent));
            }

            var rng = data.SeededRng;

            if (!rng.TryWeightedFrom(Caches.WeightedBeingData, out var patientZero)) {
                return false;
            }

            // Use a random severity between Mild and Severe (as float)
            // Clamp to closest Severity enum value
            var severityFloat = rng.Range(1.5f, 3f);
            Severity severity;
            if (severityFloat < 2f) severity = Severity.Mild;
            else if (severityFloat < 2.5f) severity = Severity.Moderate;
            else severity = Severity.Severe;
            ConditionState.CreateFor(s, ConditionIdH.FoodPoisoning, patientZero, severity, true);

            if (patientZero.Being != null) {
                s.Sys.Log.AddLine(patientZero.Being, Texts.Yellow(T.ActBreakdownfoodpoisoning));
            }
            data.TargetBeing = patientZero;
            return true;
        }
    }
}
