using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class ConnectorMalfunctionStoryEvent : StoryEvent {
        public const string EventId = "ConnectorMalfunction";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ConnectorMalfunctionStoryEvent());
        }        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;
            Caches.WeightedInts.Clear();
            foreach (var nc in A.Grid.NodeComponents) {
                var n = nc.Value;
                if (n.Node.IsConnector && n.Node.NumConnections > 0) {
                    Caches.WeightedInts.Add(n.EntityId, 1.1f - n.Tile.Health.HealthPercent);
                }
            }
            if (!data.SeededRng.TryWeightedFrom(Caches.WeightedInts, out var connectorId)) {
                data.FailEvent(S, "No eligible connector for malfunction", isHardFailure: false);
                return;
            }
            var conn = S.Components.Find<ElectricNodeComp>(connectorId);

            A.Grid.DisconnectAll(conn);

            if (conn.Tile.TryGetComponent<WithFuseComp>(out var withFuse)) {
                if (withFuse.TryBlowFuse()) {
                    DeviceMalfunctionStoryEvent.CreateMalfunctionEvent(data, conn.Tile, true);
                    data.SetState(S, StoryEventState.Ending);
                    return;
                }
            } else {
                D.Err("No WithFuseComp on {0}", conn.Tile);
            }

            conn.Tile.Health.TakeDamage(conn.Tile.Health.Health * data.SeededRng.Range(0.6f, 0.7f),
                DamageType.Energy);
            var wt = 1f;
            if (data.SeededRng.Chance(wt / 10f)) {
                S.Sys.Firex.TrySetOnFire(conn.Tile, Fire.CreateBalanced(S));
            }
            CreateConnectionLossEvent(ctx, data, conn.Tile);
            data.SetState(S, StoryEventState.Ending);
        }

        private void CreateConnectionLossEvent(StoryContext ctx, StoryEventData data, Tile connector) {
            var S = ctx.S;
            data.TargetTile = connector;
            data.CreateLogEntry(IconId.CEnergy,
                T.EvtGridMalfunctionWarning,
                T.ElDeviceHeavilyDamaged);

            Notification.BuildFor(connector, connector.Definition.Preview, T.EvtGridMalfunctionWarning,
                    Consts.TicksPer2Hours, NotificationType.Info)
                .Group("con_mal")
                .Show();
            S.Sig.ShowFanfare.Send($"{connector.NameLink}: {T.EvtGridMalfunctionWarning}");
            S.Sys.Log.AddLine(connector, T.EvtGridMalfunctionWarning);
        }
    }
}
