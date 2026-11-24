using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Systems.Firex;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;

namespace Game.Systems.Story {
    public sealed class DamagedDeviceExplodeStoryEvent : StoryEvent {
        public const string EventId = "DamagedDeviceExplode";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new DamagedDeviceExplodeStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            var foundCount = ctx.S.Components.CountMatching<ElectricNodeComp>(
                n => n.Tile.Flammable.CanExplode
                    && n.Tile.Health.IsSignificantlyDamaged == true
                    && n.IsConstructed
                    && !n.Tile.Definition.IsImportant && n.IsPowerable
                    && (n.IsProducing || n.IsConsuming || n.IsConnector), 1);

            return foundCount > 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var S = ctx.S;
            var rng = data.SeededRng;

            int found = S.Components.FindAllMatching(Caches.ElectricNodes256,
                CheckCanExplode);

            if (found < 1) {
                data.FailEvent(S, "No damaged device found for explosion", isHardFailure: false);
                return;
            }

            Array.Sort(Caches.ElectricNodes256, 0, found, EntityUtils.DamageComparer);
            var mostDamaged = Caches.ElectricNodes256[0];

            Explosion.Create(S, mostDamaged.Tile.Flammable.ExplosionLevel,
                position: mostDamaged.Tile.Position,
                flags: ExplosionFlags.SkipEvent,
                seed: rng.Int);

            data.TargetTile = mostDamaged.Tile;
            BuildLogEntry(data, mostDamaged.Tile);
            NotifyFailure(data, mostDamaged.Tile);
            data.SetState(S, StoryEventState.Ending);
        }

        private bool CheckCanExplode(ElectricNodeComp n) {
            if (!n.Tile.Flammable.CanExplode) {
                return false;
            }
            if (!n.Tile.Health.IsSignificantlyDamaged) {
                return false;
            }
            if (!n.IsConstructed) {
                return false;
            }
            if (n.Tile.Definition.IsImportant) {
                return false;
            }
            if (!n.IsPowerable) {
                return false;
            }
            if (!(n.IsProducing || n.IsConsuming || n.IsConnector)) {
                return false;
            }
            return true;
        }

        private void BuildLogEntry(StoryEventData data, Tile device) {
            var msg = T.EvtDamagedDeviceExploded;
            var desc = T.EvtDamagedDeviceExplodedDesc_V2.F(
                device.Definition.CodexLink,
                Texts.LinkTo(Pos.String(device.CenterIdx), device.CenterIdx));
            data.CreateLogEntry(IconId.CExplosion, msg, desc);
            data.TargetTile = device;
            data.ShowPopup();
        }

        private void NotifyFailure(StoryEventData data, Tile device) {
            var S = device.S;
            var msg = Texts.Red(T.EvtDamagedDeviceExploded);
            Notification.BuildFor(device, IconId.CExplosion, msg, Consts.TicksPerDay, NotificationType.Alert)
                .WithArea(device.Position, device.Size, overwriteOnHover: true)
                .WithOnClick(() => EntityUtils.CameraFocusOn(device.Position, device.Size));
            var title = Texts.LinkTo(msg, device.CenterIdx);
            S.Sig.ShowFanfare.Send(title);
            S.Sys.Log.AddLine(device, msg);
        }
    }
}
