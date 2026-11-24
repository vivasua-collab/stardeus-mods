using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class TrapFloorPage : CodexConceptPage {
        public const string PageId = "Defenses/TrapFloor";
        public override string Id => PageId;
        public override string TitleT => T.StructureFloorsTrapfloor;
        protected override string GroupTitleT => T.CodexMDefenses;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutCombatStep1_6, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Defenses_TrapFloor, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutCombatStep1_6);
        }
    }
}
