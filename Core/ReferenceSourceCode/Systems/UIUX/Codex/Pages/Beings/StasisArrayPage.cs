using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class StasisArrayPage : CodexConceptPage {
        public const string PageId = "Beings/StasisArray";
        public override string Id => PageId;
        public override string TitleT => T.ObjectsDevicesStasisarray;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, $"{Texts.Accented(T.CryoStability)} {GlyphMap.ArrowRight} {T.CryoStabilityDesc}", idx++);
            AddTextBlock(life, g, $"{Texts.Accented(T.NounAdvancedLifeSupport)} {GlyphMap.ArrowRight} {T.LifeSupportLevelDesc}", idx++);
            AddTextBlock(life, g, $"{Texts.Accented(T.StasisPreventWakeUps)} {GlyphMap.ArrowRight} {T.StasisPreventWakeUpsTip_V1.F(Units.Percentage(StasisArrayComp.PreventWakeUpThreshold))}", idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_StasisArray, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CryoStabilityDesc, T.CryostabilityUnstableWarning,
                T.StasisPreventWakeUps,
                T.LifeSupportLevelDesc, T.StasisPreventWakeUpsTip_V1);
        }
    }
}
