using Game.CodeGen;
using Game.Constants;
using Game.Data.Space;
using Game.Procgen;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class SpawnDerelictShipStoryEvent : StoryEvent {
        public const string EventId = "SpawnDerelictShip";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new SpawnDerelictShipStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (A.IsInHyperspace || !ctx.S.Sys.Sec.IsRadarActive()) {
                return false;
            }
            // this feels scripted, once player removes a derelict, this can fire instantly
            // foreach (var child in A.PlayerSector.Children) {
            //     if (child.Type.IdH == SpaceObjectTypeIdH.ShipDerelict) {
            //         return false;
            //     }
            // }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            var parent = s.Universe.Player.Parent;
            var loc = SOUtils.RandomLocationInside(parent, data.SeededRng, 0.9f);

            data.TargetSO = GenShip.NewDerelictSO(parent, loc, s, data.SeededRng, false);
            data.CreateLogEntry(GraphicsId.SpaceObjectsDerelictsShipCruiser01_Derelict_D,
                T.EvtSpawnDerelictShipTitle,
                T.EvtSpawnDerelictShipDesc);
            data.AddEventNotification(ctx, GraphicsId.SpaceObjectsDerelictsShipCruiser01_Derelict_D,
                T.EvtSpawnDerelictShipDesc);
            data.SetState(s, StoryEventState.Ending);
        }
    }
}
