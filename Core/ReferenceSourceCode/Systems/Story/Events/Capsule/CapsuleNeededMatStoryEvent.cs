using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using UnityEngine;
using Game.CodeGen;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class CapsuleNeededMatStoryEvent : StoryEvent {
        public const string EventId = "CapsuleNeededMat";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleNeededMatStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            data.HideLogEntry();
            var rng = new Rng(data.Seed);
            var mats = new Dictionary<MatType, int>();
            ctx.S.Sys.Inventory.LoadRemainingMaterials(mats);
            var weightedTypes = new Dictionary<MatType, float>();
            foreach (var m in ctx.S.Sys.Inventory.KnownMaterials) {
                if (m.IgnoreMarket) continue;
                if (m.Property == MatProperty.Nuclear) continue;
                if (m.Group != null) continue;
                int count = mats.ContainsKey(m) ? mats[m] : 0;
                if (count < 5) count = 5;
                var chance = 1f / Mathf.Sqrt(count);
                weightedTypes.Add(m, chance);
            }
            MatType matType;
            if (weightedTypes.Count == 0) {
                matType = MatType.FastGet(MatIdH.SteelPlate);
            } else {
                matType = rng.WeightedFrom(weightedTypes);
            }
            int amt = EntityUtils.GetReasonableSpawnAmountFor(matType, rng);
            var posIdx = data.TargetObj?.Obj?.PosIdx ?? 0;
            var mat = EntityUtils.SpawnRawMaterial(matType, amt, posIdx, 1f);
            if (mat != null) {
                var ff = CapsuleOpenHelper.ContainedText(data, mat.ObjLinkWithCount());
                ff = Texts.Green(ff);
                ctx.S.Sys.Log.AddLine(ff);
                WPopup.Create(false, CapsuleOpenHelper.ContainerIcon(data),
                    CapsuleOpenHelper.ContainerName(data), null, ff).Spawn(true);
            }
        }
    }
}
