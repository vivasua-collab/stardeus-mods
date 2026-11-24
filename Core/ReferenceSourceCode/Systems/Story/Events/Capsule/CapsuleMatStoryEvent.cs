using System.Collections.Generic;
using Game.Constants;
using Game.Utils;
using KL.Randomness;
using Game.Data;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class CapsuleMatStoryEvent : StoryEvent {
        public const string EventId = "CapsuleMat";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleMatStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            var rng = new Rng(data.Seed);
            var types = new Dictionary<MatType, float>();
            foreach (var mt in MatType.ByRarity) {
                if (mt.Key.IgnoreMarket) { continue; }
                types.Add(mt.Key, mt.Value);
            }
            var matType = rng.WeightedFrom(types);
            int amt = EntityUtils.GetReasonableSpawnAmountFor(matType, rng);
            var posIdx = data.TargetObj?.Obj?.PosIdx ?? 0;
            var mat = EntityUtils.SpawnRawMaterial(matType, amt, posIdx, 1f);
            var ff = CapsuleOpenHelper.ContainedText(data, mat.ObjLinkWithCount());
            ff = Texts.Green(ff);
            ctx.S.Sys.Log.AddLine(ff);
            data.HideLogEntry();
            WPopup.Create(false, CapsuleOpenHelper.ContainerIcon(data), CapsuleOpenHelper.ContainerName(data), null, ff)
                .Spawn(true);
        }
    }
}
