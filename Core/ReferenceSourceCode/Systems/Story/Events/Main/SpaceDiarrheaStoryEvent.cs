using Game.Data;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System.Collections.Generic;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Conditions;

namespace Game.Systems.Story {
    public sealed class SpaceDiarrheaStoryEvent : StoryEvent {
        public const string EventId = "SpaceDiarrhea";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new SpaceDiarrheaStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (InfectOneColonist(ctx.S)) {
                data.SetState(ctx.S, StoryEventState.Ending);
                data.CreateLogEntry(GraphicsId.DirtHumanWaste02,
                    T.EvtSpaceDiarrheaTitle, T.EvtSpaceDiarrheaDesc);
                data.AddEventNotification(ctx, GraphicsId.DirtHumanWaste02,
                    T.SpaceDiarrheaOngoing);
            } else {
                data.FailEvent(ctx.S, "Could not infect anyone", isHardFailure: false);
            }
        }


        public static bool InfectOneColonist(GameState s) {
            // select a humanoid
            Caches.WeightedBeingData.Clear();
            var beings = s.Query.AllBeingData.Ask();
            foreach (var being in beings.Values) {
                if (being.HasStateFlag(BeingState.Dead)) { continue; }
                // Alien bacteria are aboard the ship; a sealed cryo chamber is not. Same
                // guard as CanBeHacked/CanBeInspired - it also keeps the disease off anyone
                // away on an expedition.
                if (being.Body.HasFlag(BodyFlags.Missing)) { continue; }
                if (!being.Species.IsBiological) { continue; }
                if (being.HasCondition(ConditionIdH.SpaceDiarrhea)) { continue; }
                if (being.Conditions.TryGetValue(ConditionIdH.SpaceDiarrhea_Immunity,
                        out var immunity)) {
                    if (immunity.Severity > Severity.Mild) {
                        // we have enough immunity to not get sick
                        continue;
                    }
                }
                // TODO better weights, use stats
                Curve infectCurve = Tunables.DiseaseTransmissionSpeedByAgeCurve;
                Caches.WeightedBeingData.Add(being, 1f
                    + infectCurve.Evaluate(being.Body.AgePercent));
            }

            if (!s.Rng.TryWeightedFrom(Caches.WeightedBeingData, out var patientZero)) {
                return false;
            }

            ConditionState.CreateFor(s, ConditionIdH.SpaceDiarrhea, patientZero,
                Severity.Mild, true);

            // D.Warn("Infected with SpaceDiarrhea: {0}", patientZero.Name);

            if (patientZero.Being != null) {
                s.Sys.Log.AddLine(patientZero.Being, Texts.Yellow(T.SpaceDiarrheaOngoing));
            }

            // S.Situation.AddTension(0.2f, "Space Diarrhea: Infected");

            return true;
        }
    }
}
