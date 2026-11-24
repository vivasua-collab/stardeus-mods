using Game.Constants;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using Game.Data;
using Game.CodeGen;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class CapsuleObjStoryEvent : StoryEvent {
        public const string EventId = "CapsuleObj";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleObjStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            var defId = data.Def.Vars.Get(PropertyIdH.LootDefId, string.Empty);
            if (defId == string.Empty) {
                D.Err("Misconfigured story event: {0}. LootDefId string is not set!",
                    data.Def.Id);
                data.FailEvent(ctx.S, "Misconfigured event (no LootDefId)");
                return;
            }
            var def = The.Defs.Get(defId);
            if (def == null) {
                D.Err("Misconfigured story event: {0}. Def does not exist: {1} (LootDefId)",
                    data.Def.Id, defId);
                data.FailEvent(ctx.S, "Misconfigured event (def missing)");
                return;
            }
            var rng = new Rng(data.Seed);
            var pos = EntityUtils.CenterOf(data.TargetObj?.Obj?.PosIdx ?? 0);
            var obj1 = ctx.S.Objs.Create(pos + Rng.UInsideUnitCircle(), def);
            var ff = CapsuleOpenHelper.ContainedText(data, Texts.LinkToObj(obj1));
            ff = Texts.Green(ff);
            ctx.S.Sys.Log.AddLine(ff);
            data.HideLogEntry();
            WPopup.Create(false, CapsuleOpenHelper.ContainerIcon(data),
                CapsuleOpenHelper.ContainerName(data), null, ff).Spawn(true);
        }
    }
}
