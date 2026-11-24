using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Data.Factions;

namespace Game.Systems.Story {
    public sealed class InjuredRaiderJoinedColonyStoryEvent : StoryEvent {
        [SelfInit]
        private static void Register() {
            Register(new InjuredRaiderJoinedColonyStoryEvent());
        }

        public const string EventId = "InjuredRaiderJoinedColony";
        public override string Id => EventId;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var allBeings = ctx.S.Query.AllBeingData.Ask();
            foreach (var being in allBeings.Values) {
                if (IsGoodCandidate(being)) {
                    return true;
                }
            }
            return false;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            Caches.WeightedBeingData.Clear();
            var allBeings = s.Query.AllBeingData.Ask();
            var rng = data.SeededRng;
            foreach (var being in allBeings.Values) {
                if (!IsGoodCandidate(being)) {
                    continue;
                }
                Caches.WeightedBeingData.Add(being, rng.Value + 0.1f);
            }

            if (!rng.TryWeightedFrom(Caches.WeightedBeingData, out var victim)) {
                data.FailEvent(s, "No eligible beings found to join the colony",
                    isHardFailure: false);
                // can't do this
                // ctx.CooldownEvent(data);
                return;
            }

            Caches.WeightedBeingData.Clear();

            data.TargetBeing = victim;
            Notify(ctx, data, victim);
            victim.Mind.SetFaction(FactionDef.Player);
            // Operations ordered for a prisoner (a Remove All harvest, most of all) are not
            // orders for a crew member. A surgery hub then has nothing left to do and lets the
            // new colonist go, so they can be treated (STA-1779).
            s.Query.ClearSurgicalOpsFor.Ask(victim);
            The.AchievementTracker.Unlock(
                Achievements.AchievementDefection.AchievementId);
            data.SetState(s, StoryEventState.Ending);
        }

        private void Notify(StoryContext ctx, StoryEventData data, BeingData victim) {
            BuildLogEntry(data, victim);
            data.TargetBeing = victim;
            data.AddEventNotification(ctx, FactionDef.Player.FlagIcon, T.EvInjuredRaiderJoinedColony,
                showPopup: true, showFanfare: true, addLogLine: true);
        }

        private void BuildLogEntry(StoryEventData data, BeingData victim) {
            data.CreateLogEntry(FactionDef.Player.FlagIcon,
                T.EvInjuredRaiderJoinedColony, T.EvInjuredRaiderJoinedColonyDesc_V2.F(
                    victim.Mind.LinkToShortName, Texts.LinkToFactionTooltip(victim.Mind.Faction)));
        }

        // InDevice is still here: a raider lying in a surgery hub can swear allegiance
        private const BeingState NotHere = BeingState.Hidden | BeingState.Stored
            | BeingState.Expedition | BeingState.InColony;

        private static bool IsGoodCandidate(BeingData being) {
            if (being.IsPlayer) { return false; }
            // Only a raider lying here: one that left the map (escaped, left behind on a
            // jump) keeps its data as Hidden without an entity, and turning it made a crew
            // member that exists nowhere (STA-1882)
            if (being.Being == null || !being.Being.IsActive) { return false; }
            if ((being.StateFlags & NotHere) != 0) { return false; }
            if (being.HasStateFlag(BeingState.Dead)) { return false; }
            if (being.Body.HasFlag(BodyFlags.Missing)) { return false; }
            if (being.Mind.Species.IsAnimal) { return false; }
            if (!being.Mind.Species.IsBiological) { return false; }
            if (!being.Body.IsIncapacitated) { return false; }
            return true;
        }
    }
}
