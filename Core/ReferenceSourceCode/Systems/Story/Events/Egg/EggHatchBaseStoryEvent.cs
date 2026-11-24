using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Story;

namespace Game.Systems.Story.Events {
    public abstract class EggHatchBaseStoryEvent : StoryEvent {
        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var obj = data.TargetObj?.Obj;
            if (obj == null || !obj.IsActive) {
                D.Err("Egg not available! {0}", obj);
                data.FailEvent(ctx.S, "Egg not available");
                return;
            }
            if (!obj.TryGetComponent<EggComp>(out var egg)) {
                D.Err("Entity has no EggComp: {0}", obj);
                data.FailEvent(ctx.S, "No EggComp");
                return;
            }
            if (!egg.TryHatch(ctx.S.Ticks)) {
                data.FailEvent(ctx.S, "Egg failed to hatch");
                return;
            }
            DoSpawn(ctx, data, egg, obj.Position);
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        protected abstract void DoSpawn(StoryContext ctx, StoryEventData data, EggComp egg, Vector2 eggPos);
    }
}
