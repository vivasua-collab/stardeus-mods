using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using Game.CodeGen;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class CapsuleShuttleIngredientsStoryEvent : StoryEvent {
        public const string EventId = "CapsuleShuttleIngredients";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleShuttleIngredientsStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Components.CountMatching<ShuttleComp>(
                sh => sh.IsConstructed && !sh.IsLost, max: 1) == 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            data.HideLogEntry();
            var rng = new Rng(data.Seed);
            var mats = new Dictionary<MatType, int>();
            ctx.S.Sys.Inventory.LoadRemainingMaterials(mats);
            var posIdx = data.TargetObj?.Obj?.PosIdx ?? 0;
            var def = The.Defs.FastGet(DefIdsH.ObjectsDevicesShuttle);
            MatType deficitMat = null;
            int biggestDeficit = 0;

            foreach (var req in def.ConstructedMats) {
                int deficit = req.MaxStackSize;
                if (!mats.TryGetValue(req.Type, out var curAmt)) {
                    deficit -= curAmt;
                }
                if (biggestDeficit > deficit) {
                    continue;
                }
                biggestDeficit = deficit;
                deficitMat = req.Type;
            }

            int amt;
            if (deficitMat == null) {
                deficitMat = MatType.FastGet(MatIdH.Rock);
                amt = 30;
            } else {
                if (biggestDeficit % 10 != 0) {
                    amt = 20 + biggestDeficit - (biggestDeficit % 10);
                } else {
                    amt = 10 + biggestDeficit;
                }
            }
            var mat = EntityUtils.SpawnRawMaterial(deficitMat, amt, posIdx, 1f);
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
