using Game.Components;
using Game.Constants;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Data;
using Game.CodeGen;
using Game.Input;

namespace Game.Systems.Story {
    public class CapsuleBombStoryEvent : StoryEvent {
        public const string EventId = "CapsuleBomb";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleBombStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var being = data.TargetBeing;
            if (being == null) {
                data.FailEvent(ctx.S, "CapsuleBombStoryEvent has no being!");
                return;
            }
            data.SetState(ctx.S, StoryEventState.Ending);
            var bomb = BombComp.Spawn(ctx.S, being.Being.Motion.Position.Exact);
            var fac = ctx.S.Sys.Diplomacy.RandomFactionForRaid();
            bomb.Faction = fac;
            bomb.SetTimer(ctx.S.Rng.Range(Consts.TicksPer30Minutes, Consts.TicksPerHour));
            bomb.PlayPreDetonateSound(true);
            var ff = Texts.Red(CapsuleOpenHelper.ContainedText(data, bomb.Obj.Definition.NameT));
            ctx.S.Sys.Log.AddLine(ff);
            data.CreateLogEntry(GraphicsId.ObjMiscC42_Armed,
                $"{CapsuleOpenHelper.ContainerName(data)} {GlyphMap.ArrowRight} {T.ObjMiscC42}",
                CapsuleOpenHelper.ContainedText(data,
                    Texts.LinkToCodex(DefIdsH.ObjMiscC42)));
            data.ShowPopup();
            PauseClock();
            EntityUtils.CameraFocusOn(bomb.Obj);
        }
    }
}
