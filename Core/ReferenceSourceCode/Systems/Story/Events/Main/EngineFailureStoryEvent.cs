using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using Game.Systems.Firex;

namespace Game.Systems.Story {
    public sealed class EngineFailureStoryEvent : StoryEvent {
        public const string EventId = "EngineFailure";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new EngineFailureStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Sys.ShipNav.Engines.Any(
                e => !e.IsManeuvering && e.Hyperdrive == null);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (!CheckConditions(ctx, data.Def)) {
                data.FailEvent(ctx.S, "No engines for failure", isHardFailure: false);
            }
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (ctx.S.Ticks % 48 != 0) return;
            TryBlowEngine(ctx, data);
        }

        private void TryBlowEngine(StoryContext ctx, StoryEventData data) {
            var candidates = ctx.S.Sys.ShipNav.Engines.Where(e => e.Hyperdrive == null && e.ENode.IsConsuming);
            candidates = candidates.OrderByDescending(
                e => (e.Tile.GetComponent<WithFuseComp>()?.CurrentBreakChance ?? 1f) + e.ExtraExplosionChance * 5f)
                .ThenBy(_ => ctx.Rng.Value);
            var selectedEngine = candidates.FirstOrDefault();
            if (selectedEngine == null) { return; }
            FailEngine(ctx, data, selectedEngine);
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        private void FailEngine(StoryContext ctx, StoryEventData data, ShipEngineComp selectedEngine) {
            var S = ctx.S;
            if (selectedEngine.Tile.TryGetComponent<WithFuseComp>(out var withFuse)) {
                if (withFuse.TryBlowFuse()) {
                    DeviceMalfunctionStoryEvent.CreateMalfunctionEvent(data,
                        selectedEngine.Tile, true);
                    return;
                }
            } else {
                D.Err("No WithFuseComp on {0}", selectedEngine.Tile);
            }
            selectedEngine.Tile.Flammable.CreateExplosion(false);
            data.CreateLogEntry(IconId.CExplosion, T.EvtEngineFailureTitle,
                T.EvtEngineFailureDesc_V1.F(Texts.LinkToTile(selectedEngine.Tile)));
            var text = Texts.LinkTo(Texts.LinkColor(T.EvtEngineFailureTitle), selectedEngine.Entity.PosIdx);
            S.Sig.ShowFanfare.Send(text);
            S.Sys.Log.AddLine(text);
        }
    }
}
