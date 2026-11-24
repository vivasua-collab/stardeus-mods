using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class TractorBeamPage : CodexConceptPage {
        public const string PageId = "Defenses/TractorBeam";
        public override string Id => PageId;
        public override string TitleT => T.TractorBeam;
        protected override string GroupTitleT => T.CodexMDefenses;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMDefensesTractorbeamDesc_New, idx++);
            AddTextBlock(life, g, T.CodexMDefensesTractorbeamDesc2, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Defenses_TractorBeam, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMDefensesTractorbeamDesc_New,
                T.CodexMDefensesTractorbeamDesc2);
        }
    }
}
