using KL.Utils;
using UnityEngine;
using Game.Systems.AI.Combat;
using Game.Components;
using Game.Constants;
using Game.Utils;

namespace Game.Systems.Story {
    public class StasisPodHostileBeingsStoryEvent : StoryEvent {

        public const string EventId = "StasisPodHostileBeings";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new StasisPodHostileBeingsStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            var raid = new CombatRaid(ctx.S, pod.ContentsSeed);
            raid.MaxRaiders = 8;
            // Thematic pod encounter, see StasisPodRaidStoryEvent.
            raid.CountsAsRaid = false;
            data.Faction ??= ctx.S.Sys.Diplomacy.RandomFactionForRaid();
            raid.Faction = data.Faction;
            raid.PosIdx = pod.Tile.Transform.AnyPortOutFirst;
            if (raid.TryStartRaid()) {
                data.SetState(ctx.S, StoryEventState.Ending);
                var fac = data.Faction;
                data.CreateLogEntry(fac.FlagIcon,
                    T.EvtStasisPodRaidTitle_V1.F(fac.NameT),
                    T.EvtStasisPodRaidDesc_V1.F(fac.NameT));
            } else {
                D.Err("Failed starting a raid!", data, this);
                data.FailEvent(ctx.S, "Failed to start a raid");
            }
        }
    }
}
