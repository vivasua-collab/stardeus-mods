using Game.Constants;
using Game.Utils;
using KL.Randomness;
using Game.Data;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class CapsuleEquipmentSetStoryEvent : StoryEvent {
        public const string EventId = "CapsuleEquipmentSet";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleEquipmentSetStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            var rng = new Rng(data.Seed);
            var helmetDef = The.Defs.RandomMatching(DefIds.ObjEquipmentHatsSpaceHelmet01.Replace("01", ""), rng);
            var suitDef = The.Defs.RandomMatching(DefIds.ObjEquipmentClothingSpaceSuit01.Replace("01", ""), rng);
            var bodyDef = The.Defs.RandomMatching(DefIds.ObjEquipmentClothingUniform01.Replace("01", ""), rng);
            var pos = EntityUtils.CenterOf(data.TargetObj?.Obj?.PosIdx ?? 0);
            var obj1 = ctx.S.Objs.Create(pos + Rng.UInsideUnitCircle(), helmetDef);
            var obj2 = ctx.S.Objs.Create(pos + Rng.UInsideUnitCircle(), suitDef);
            var obj3 = ctx.S.Objs.Create(pos + Rng.UInsideUnitCircle(), bodyDef);
            var contained = $"\n - {Texts.LinkToObj(obj1)}\n - {Texts.LinkToObj(obj2)}\n - {Texts.LinkToObj(obj3)}";
            var ff = CapsuleOpenHelper.ContainedText(data, contained);
            ff = Texts.Green(ff);
            ctx.S.Sys.Log.AddLine(ff);
            data.HideLogEntry();
            WPopup.Create(false, CapsuleOpenHelper.ContainerIcon(data), CapsuleOpenHelper.ContainerName(data), null, ff)
                .Spawn(true);
        }
    }
}
