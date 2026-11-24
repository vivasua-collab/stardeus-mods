using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class DeviceMalfunctionStoryEvent : StoryEvent {
        public const string EventId = "DeviceMalfunction";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DeviceMalfunctionStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;
            Caches.WeightedInts.Clear();
            var withFuse = S.Components.AllByType<WithFuseComp>();
            if (Arrays.IsEmpty(withFuse)) {
                data.FailEvent(S, "Nothing with fuse comp", isHardFailure: false);
                return;
            }
            foreach (WithFuseComp mt in S.Components.AllByType<WithFuseComp>()) {
                if (!mt.CanHaveGenericMalfunction) { continue; }
                if (!mt.Tile.Flammable.CanExplode) { continue; }
                var chance = mt.CurrentBreakChance;
                if (chance == 0f) { continue; }
                Caches.WeightedInts.Add(mt.EntityId, chance);
            }
            if (!data.SeededRng.TryWeightedFrom(Caches.WeightedInts, out var targetId)) {
                data.FailEvent(S, "No eligible device for malfunction", isHardFailure: false);
                return;
            }
            var target = S.Components.Find<WithFuseComp>(targetId);
            var device = target.Tile;
            var blownFuse = false;
            target.DoMalfunction(out blownFuse);
            CreateMalfunctionEvent(data, device, blownFuse);
            data.SetState(S, StoryEventState.Ending);
        }

        public static void CreateMalfunctionEvent(StoryEventData data, Tile device, bool blownFuse) {
            var S = device.S;
            string msg;
            string logMsg;
            data.TargetTile = device;
            if (blownFuse) {
                msg = T.EvtBlownFuse_V1.F(device.NameOrLabel);
                logMsg = T.EvtBlownFuse_V1.F(device.NameLink);
                data.CreateLogEntry(GraphicsId.ObjUpgradesFuseUpgrade, msg, T.EvtBlownFuseDesc);
            } else {
                msg = T.EvtDeviceMalfunction_V1.F(device.NameOrLabel);
                logMsg = T.EvtDeviceMalfunction_V1.F(device.NameLink);
                data.CreateLogEntry(IconId.CExplosion, msg,
                    T.EvtDeviceMalfunctionDesc_V1.F(device.NameLink));
            }

            Notification.BuildFor(device, device.Definition.Preview, data.LogEntry.Title, Consts.TicksPerDay)
                .Group("inc", T.NounIncident, IconId.CWarning)
                .WithOnClick(() => EntityUtils.CameraFocusOn(device))
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            S.Sig.ShowFanfare.Send(logMsg);
            S.Sys.Log.AddLine(logMsg);
        }
    }
}
