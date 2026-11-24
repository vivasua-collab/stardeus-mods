using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI.Combat;

namespace Game.Systems.Story {
    public sealed class MapEdgeRaidStoryEvent : StoryEvent {
        public override string Id => "MapEdgeRaid";
        [SelfInit]
        private static void Register() {
            Register(new MapEdgeRaidStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn && !A.IsInHyperspace;
        }


        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!A.IsCombatOn) {
                data.HideLogEntry();
                data.SetState(s, StoryEventState.Ending);
                return;
            }
            var rng = data.SeededRng;
            var raid = new CombatRaid(s, rng.Int);
            var pos = EntityUtils.RandomMapEdgePoint(rng, offset: 0.05f);
            raid.PosIdx = Pos.FromVector(pos);
            if (raid.PosIdx <= 0) {
                raid.PosIdx = 1;
            }
            raid.ForceFlyingOnly = true;
            // A staged raid: some raids gather at the edge first and attack together
            // (CombatObjectiveStage). The fight, and its alert, start when they attack or when
            // the crew finds them first; until then a gathering notice says they are there
            // (GatheringRaidNotice).
            var vars = data.Def.Vars;
            var staging = rng.Chance(vars.Get(PropertyIdH.StagingChance, 0f));
            if (staging) {
                var hours = rng.Range(new Vector2(vars.Get(PropertyIdH.StagingHoursMin, 2f),
                    vars.Get(PropertyIdH.StagingHoursMax, 6f)));
                raid.StageUntilTick = s.Ticks + (long) (hours * Consts.TicksPerHour);
            }
            if (!raid.TryStartRaid(out var army)) {
                data.FailEvent(s, "Failed starting raid", isHardFailure: A.IsCombatOn);
                return;
            }
            data.Faction = raid.Faction;
            if (staging) {
                data.CreateLogEntry(IconId.CCombat,
                    T.EvRaidMapEdgeStagingTitle, T.EvRaidMapEdgeStagingDesc);
                foreach (var unit in army.Units) {
                    data.AddParticipant(unit.Agent.BeingData);
                }
                data.SetState(s, StoryEventState.Ending);
                return;
            }
            // The raid alert (CombatSys) shows these same texts as the raiders set off, with the
            // siren and the callout, and stays up until they meet the crew
            data.CreateLogEntry(IconId.CCombat,
                T.EvRaidMapEdgeTitle, T.EvRaidMapEdgeDesc);
            foreach (var unit in army.Units) {
                data.AddParticipant(unit.Agent.BeingData);
            }
            data.SetState(s, StoryEventState.Ending);
        }
    }
}
