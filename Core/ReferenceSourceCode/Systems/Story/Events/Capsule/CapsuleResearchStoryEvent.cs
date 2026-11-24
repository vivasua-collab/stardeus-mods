using Game.Constants;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Data;
using Game.CodeGen;
using Game.UI;
using Game.Research;
using Game.Input;

namespace Game.Systems.Story {
    public class CapsuleResearchStoryEvent : StoryEvent {
        public const string EventId = "CapsuleResearch";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new CapsuleResearchStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ctx.S.Research.NumLocked > 0;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.SetState(ctx.S, StoryEventState.Ending);
            var rng = new Rng(data.Seed);

            var minTier = ctx.S.Research.MaxTier;
            var maxTier = minTier + 1;

            var res = ctx.S.Research;

            var wlist = Caches.WeightedInts;
            wlist.Clear();

            foreach (var rdef in ResearchTree.All) {
                if (rdef.Tier < minTier || rdef.Tier > maxTier) { continue; }
                if (res.IsUnlocked(rdef.IdH)) { continue; }
                if (!res.IsDiscovered(rdef.IdH)) { continue; }
                if (rdef.Tier == minTier) {
                    wlist.Add(rdef.IdH, 1f);
                } else {
                    // higher tier
                    wlist.Add(rdef.IdH, 0.5f);
                }
            }

            if (!rng.TryWeightedFrom(wlist, out int defIdH)) {
                data.FailEvent(ctx.S, "Couldn't pick unlocked research");
                return;
            }

            var def = The.Defs.FastGet(defIdH);
            if (!res.TryUnlockBonus(defIdH)) {
                data.FailEvent(ctx.S, $"Couldn't unlock research: {def}");
                return;
            }
            var ff = CapsuleOpenHelper.ContainedText(data, $"{T.OverlayTipResearch}: {Texts.LinkToResearch(def)}");
            data.CreateLogEntry(def.Preview, $"{CapsuleOpenHelper.ContainerName(data)} {GlyphMap.ArrowRight} {T.OverlayTipResearch}", ff);
            data.ShowPopup();
            ff = Texts.Green(ff);
            ctx.S.Sys.Log.AddLine(ff);
        }
    }
}
