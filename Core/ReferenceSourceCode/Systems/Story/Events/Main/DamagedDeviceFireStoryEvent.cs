using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Firex;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class DamagedDeviceFireStoryEvent : StoryEvent {
        public const string EventId = "DamagedDeviceFire";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new DamagedDeviceFireStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var foundCount = ctx.S.Components.CountMatching<ElectricNodeComp>(
                n => n.Tile.Health.IsSignificantlyDamaged == true
                    && n.IsConstructed
                    && !n.Tile.Definition.IsImportant && n.IsPowerable
                    && (n.IsProducing || n.IsConsuming || n.IsConnector)
                    && ctx.S.GetO2(n.Tile.CenterIdx) > Consts.MinOxygen, 1);

            return foundCount > 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;
            int found = S.Components.FindAllMatching(Caches.ElectricNodes256,
                n => n.Tile.Health.IsSignificantlyDamaged
                    && n.IsConstructed
                    && !n.Tile.Definition.IsImportant && n.IsPowerable
                    && S.Sys.Firex.CanIgnite(n.Tile.Flammable)
                    && (n.IsProducing || n.IsConsuming || n.IsConnector)
                    && S.GetO2(n.Tile.CenterIdx) > Consts.MinOxygen);

            if (found < 1) {
                data.FailEvent(S, "No damaged device found for fire", isHardFailure: false);
                return;
            }

            Array.Sort(Caches.ElectricNodes256, 0, found, EntityUtils.DamageComparer);
            var mostDamaged = Caches.ElectricNodes256[0];

            if (!S.Sys.Firex.TrySetOnFire(mostDamaged.Tile, Fire.CreateBalanced(S, 1.2f, fuel: 0.25f))) {
                data.FailEvent(S, "Could not set device on fire", isHardFailure: true);
                return;
            }

            data.TargetTile = mostDamaged.Tile;
            BuildLogEntry(data, mostDamaged.Tile);
            NotifyFailure(data, mostDamaged.Tile);
            data.SetState(S, StoryEventState.Ending);
        }

        private void BuildLogEntry(StoryEventData data, Tile device) {
            var msg = T.EvtDamagedDeviceFire;
            var desc = T.EvtDamagedDeviceFireDesc_V2.F(device.Definition.CodexLink,
                Texts.LinkTo(Pos.String(device.CenterIdx), device.CenterIdx));
            data.CreateLogEntry(IconId.CFire, msg, desc);
            data.ShowPopup();
        }

        private void NotifyFailure(StoryEventData data, Tile device) {
            var S = device.S;

            Notification.BuildFor(device, IconId.CFire, data.LogEntry.Title, Consts.TicksPerDay)
                .Group("inc", T.NounIncident, IconId.CWarning)
                .WithArea(device.Position, device.Size, overwriteOnHover: true)
                .WithOnClick(() => EntityUtils.CameraFocusOn(device))
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            var msg = Texts.Red(T.EvtDamagedDeviceFire);
            S.Sig.ShowFanfare.Send(Texts.LinkToTile(device, msg));
            S.Sys.Log.AddLine(device, msg);
        }
    }
}
