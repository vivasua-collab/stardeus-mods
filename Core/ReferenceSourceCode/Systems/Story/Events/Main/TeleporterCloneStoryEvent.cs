using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Story;
using Game.Utils;
using Game.CodeGen;
using KL.Signals;
using KL.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using Game.Systems.Items;

namespace Game.Systems.Story {
    public sealed class TeleporterCloneStoryEvent : StoryEvent {
        public const string EventId = "TeleporterClone";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new TeleporterCloneStoryEvent());
        }

        private static bool IsTeleportersAvailable(GameState s) {
            return s.Components.CountMatching<TeleporterComp>(
                t => t.CanTeleport && t.Outbound != null) > 1;
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return IsTeleportersAvailable(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (!IsTeleportersAvailable(ctx.S)) {
                data.FailEvent(ctx.S, "Teleporters no longer available at OnStart", isHardFailure: false);
                return;
            }

            Listen(ctx, data);
            data.SetState(ctx.S, StoryEventState.WaitingForHook);
        }

        // The listener is not saved: without this, an event saved while waiting for a
        // teleport never fired after a load and soft-failed on its timeout.
        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            if (data.State == StoryEventState.WaitingForHook) {
                Listen(ctx, data);
            }
        }

        private void Listen(StoryContext ctx, StoryEventData data) {
            if (data.EventListener != null) { return; }
            var listener = ctx.S.Sig.EntityTeleportEnded.AddListener(
                (entity, teleporterComp) => OnEntityTeleported(ctx, data, entity, teleporterComp)
            );
            data.EventListener = listener;
        }

        private void OnEntityTeleported(StoryContext ctx, StoryEventData data, Entity e, TeleporterComp to) {
            if (e is not Being b || !b.Species.IsBiological) {
                return;
            }

            // never clone a clone
            if (b.Data.Body.HasFlag(BodyFlags.Clone)) {
                return;
            }

            if (data.IsEnded || data.State == StoryEventState.Ending) {
                return;
            }

            var s = ctx.S;
            var dataCopy = BeingDataData.DeepCopy(s, b.Data);
            if (dataCopy.Inventory != null) {
                foreach (var item in dataCopy.Inventory.Equipment) {
                    if (item.Value.Item != null) {
                        var obData = item.Value.Unequip();
                        obData.State = ObjState.Destroyed;
                    }
                }
                dataCopy.Inventory.NeedsStatsUpdate = true;
                dataCopy.Inventory.Update();
            }
            dataCopy.Body.Flags |= BodyFlags.Clone;
            dataCopy.RestoreConditions(s, true);

            var clone = s.Beings.Create(e.Position, dataCopy);

            data.TargetBeing = clone.Data;

            data.CreateLogEntry(
                IconId.CCloning,
                T.EvtTeleporterCloneTitle,
                T.EvtTeleporterCloneDesc_V1.F(Texts.LinkToBeing(b.Data))
            );

            var ev = data.AddEventNotification(ctx, IconId.CCloning, T.EvtTeleporterCloneTitle);

            var title = Texts.LinkToBeing(clone.Data, Texts.Yellow(T.EvtTeleporterCloneTitle));
            s.Sig.ShowFanfare.Send(title);
            s.Sys.Log.AddLine(clone, Texts.Yellow(T.EvtTeleporterCloneTitle));
            The.AchievementTracker.Unlock(
                Achievements.AchievementTeleporterClone.AchievementId);

            data.SetState(s, StoryEventState.Ending);

            if (data.EventListener is Signal2<Entity, TeleporterComp>.SignalListener sigListener) {
                s.Sig.EntityTeleportEnded.RemoveListener(sigListener);
                data.EventListener = null;
            }
        }

        protected override void OnEnd(StoryContext ctx, StoryEventData data) {
            if (data.EventListener is Signal2<Entity, TeleporterComp>.SignalListener listener) {
                D.Warn("TeleporterCloneStoryEvent.OnEnd: Removing listener that should have been removed by OnEntityTeleported. Event Def: {0}", data.Def?.Id ?? "N/A");
                ctx.S.Sig.EntityTeleportEnded.RemoveListener(listener);
                data.EventListener = null;
            }
            base.OnEnd(ctx, data);
        }
    }
}
