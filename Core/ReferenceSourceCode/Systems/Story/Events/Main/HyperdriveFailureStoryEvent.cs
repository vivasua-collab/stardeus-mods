using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class HyperdriveFailureStoryEvent : StoryEvent {
        public const string EventId = "HyperdriveFailure";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new HyperdriveFailureStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Sys.ShipNav.Engines.Any(e => e.WorksIn(UniverseLayer.Region)
                || e.WorksIn(UniverseLayer.Universe));
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            SubscribeToJump(ctx, data);
        }

        public override void OnLoad(StoryContext ctx, StoryEventData data) {
            SubscribeToJump(ctx, data);
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            SubscribeToJump(ctx, data);
        }

        private void SubscribeToJump(StoryContext ctx, StoryEventData data) {
            if (data.EventListener != null) { return; }
            if (ctx.S.Components.CountMatching<HyperdriveComp>(d => d.IsConstructed, 1) == 0) {
                data.FailEvent(ctx.S, "No hyperdrives", isHardFailure: false);
                return;
            }
            var listener = ctx.S.Sig.HyperjumpExecuted.AddListener((HyperdriveComp drive) => OnHyperjump(ctx, data, drive));
            data.EventListener = listener;
            data.SetState(ctx.S, StoryEventState.WaitingForHook);
        }

        private void OnHyperjump(StoryContext ctx, StoryEventData data, HyperdriveComp drive) {
            var engine = drive.Engine;
            var S = ctx.S;
            if (engine.Tile.TryGetComponent<WithFuseComp>(out var withFuse)) {
                if (withFuse.TryBlowFuse()) {
                    // DeviceMalfunctionStoryEvent.CreateMalfunctionEvent(engine.Tile, true);
                    // If you have a new malfunction event, trigger it here
                    RemoveListener(ctx, data);
                    data.SetState(ctx.S, StoryEventState.Ending);
                    return;
                }
            } else {
                D.Err("No WithFuseComp on {0}", engine.Tile);
            }
            engine.Tile.Flammable.CreateExplosion(false);
            RemoveListener(ctx, data);
            data.CreateLogEntry(IconId.CExplosion, T.EvtEngineFailureTitle,
                T.EvtEngineFailureDesc_V1.F(Texts.LinkToTile(engine.Tile)));
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        private void RemoveListener(StoryContext ctx, StoryEventData data) {
            if (data.EventListener is KL.Signals.Signal1<HyperdriveComp>.SignalListener listener) {
                ctx.S.Sig.HyperjumpExecuted.RemoveListener(listener);
                data.EventListener = null;
            }
        }
    }
}
