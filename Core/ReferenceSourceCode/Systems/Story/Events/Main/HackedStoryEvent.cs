using System;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;

namespace Game.Systems.Story {
    public sealed class HackedStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new HackedStoryEvent());
        }

        public const string EventId = "Hacked";
        public override string Id => EventId;

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            Caches.WeightedBeingData.Clear();
            var allBeings = s.Query.AllBeingData.Ask();
            foreach (var being in allBeings.Values) {
                if (!CanBeHacked(being)) {
                    continue;
                }
                Caches.WeightedBeingData.Add(being, 200f - being.Mind.Age);
            }
            var rng = data.SeededRng;

            if (!rng.TryWeightedFrom(Caches.WeightedBeingData, out var victim)) {
                data.FailEvent(s, "No eligible beings found to hack", isHardFailure: false);
                return;
            }

            Caches.WeightedBeingData.Clear();

            if (ConditionState.CreateFor(s, ConditionIdH.Hacked, victim, Severity.Mild, true)) {
                if (victim.Being?.AIAgent.DirectControl.IsUnderDirectControl == true) {
                    victim.Being.AIAgent.DirectControl.SetDirectControl(false);
                }
                data.TargetBeing = victim;
                NotifyHacked(ctx, data, victim);
                victim.StatsBeing.AIAlignment.Apply();
                BeingUtils.UpdateFlagsFor(s, victim);
                data.SetState(s, StoryEventState.Ending);
            } else {
                D.Err("Could not create hacked condition for being: {0}", victim);
                data.FailEvent(s, "Could not create hacked condition");
            }
        }

        private void NotifyHacked(StoryContext ctx, StoryEventData data, BeingData victim) {
            BuildLogEntry(data, victim);
            data.AddEventNotification(ctx, IconId.IconsFactionVirus, T.VerbHacked);
            data.TargetBeing = victim;

            var name = Texts.LinkToBeing(victim);
            var msg = T.EvtHacked_V1.F(name);
            ctx.S.Sig.ShowFanfare.Send(msg);
            ctx.S.Sys.Log.AddLine(msg);
        }

        private void BuildLogEntry(StoryEventData data, BeingData victim) {
            var evDesc = T.CondHackedDesc;
            data.CreateLogEntry(IconId.IconsFactionVirus, T.EvtHacked_V1.F(victim.Name), evDesc);
        }

        // Robots only: an organic with an Obey Chip used to qualify too, which read as a
        // person being hacked like a machine (STA-1920)
        private static bool CanBeHacked(BeingData being) {
            if (being.HasStateFlag(BeingState.Dead)) { return false; }
            if (being.Body.HasFlag(BodyFlags.Missing)) { return false; }
            if (being.Species.IsBiological) { return false; }
            if (being.HasCondition(ConditionIdH.Hacked)) { return false; }
            if (!being.IsPlayer) { return false; }
            if (BodyUtils.HasWorkingImplant(being,
                    DefIdsH.ObjPartsImplantFirewallChip)) {
                return false;
            }
            return true;
        }
    }
}
