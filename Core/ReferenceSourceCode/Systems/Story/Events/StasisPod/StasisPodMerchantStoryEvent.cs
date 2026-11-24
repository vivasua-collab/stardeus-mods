using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System.Linq;
using Game.Systems.Naming;
using Game.Input;

namespace Game.Systems.Story {
    public sealed class StasisPodMerchantStoryEvent : StoryEvent {
        public const string EventId = "StasisPodMerchant";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new StasisPodMerchantStoryEvent());
        }

        private void CreateLogEntry(StoryEventData data) {
            data.CreateLogEntry(IconId.CStarcreds,
                T.EvtStasisPodMerchantTitle,
                T.EvtStasisPodMerchantDesc);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            if (pod.Entity.TryGetComponent<StasisPodMerchantComp>(out var merchant)) {
                merchant.IsTraderActive = true;
                merchant.Seed = data.Seed;
                pod.Open();
                merchant.SetWillingToTrade(true);

                CreateLogEntry(data);

                var podTile = pod.Tile;
                Notification.BuildFor(podTile, IconId.CStarcreds, data.LogEntry.Title, Consts.TicksPerDay, NotificationType.Immediate)
                    .WithOnClick(() => {
                        if (podTile.IsActive) {
                            EntityUtils.SelectOrFocus(podTile);
                        }
                        data.ShowPopup();
                    })
                    .WithTooltip(data.LogEntry.Desc)
                    .Show();

                s.Sig.ShowFanfare.Send(Texts.LinkToTile(pod.Tile,
                    Texts.Yellow(T.EvtStasisPodMerchantTitle)));
                s.Sys.Log.AddLine(pod.Tile, T.EvtStasisPodMerchantTitle);
            } else {
                data.FailEvent(s, "No StasisPodMerchantComp on pod merchant event");
                pod?.Launch();
            }
        }
    }
}
