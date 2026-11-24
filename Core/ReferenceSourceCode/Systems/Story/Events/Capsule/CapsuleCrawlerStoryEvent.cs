using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using UnityEngine;
using Game.Systems.Naming;
using Game.CodeGen;
using Game.Input;

namespace Game.Systems.Story {
    public class CapsuleCrawlerStoryEvent : StoryEvent {
        public const string EventId = "CapsuleCrawler";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleCrawlerStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            var rng = data.SeededRng;
            var species = Species.Get(SpeciesIdH.Crawler);
            var vars = new GenBeingVars(species, BeingType.Neutral);
            // vars.Body = FactionDef.RandomBodyVars(rng);
            int cnt = rng.Range(1, Mathf.Clamp(Mathf.FloorToInt(ctx.S.Difficulty.Level * 4), 2, 5));
            var beingData = ctx.S.Query.GenBeingFull.Ask(vars, rng);
            var posIdx = data.TargetObj?.Obj?.PosIdx ?? 0;
            var being = ctx.S.Beings.Create(EntityUtils.CenterOf(posIdx), beingData);
            if (being == null) {
                data.FailEvent(ctx.S, "Failed to spawn crawler");
                return;
            }
            var ff = CapsuleOpenHelper.ContainedText(data,
                Texts.LinkToBeing(being.Data, being.Species.Name));
            ff = Texts.Green(ff);
            ctx.S.Sys.Log.AddLine(ff);
            data.TargetBeing = being.Data;
            data.CreateLogEntry(
                being.Definition.Preview,
                $"{CapsuleOpenHelper.ContainerName(data)} {GlyphMap.ArrowRight} {being.Species.Name}",
                T.EvtCapsuleBeingDesc_V1.F(being.Data.Mind.Name.DisplayName));
            data.ShowPopup();
            if (A.FocusOnEvents) {
                PauseClock();
                EntityUtils.CameraFocusOn(being, true);
            }
        }
    }
}
