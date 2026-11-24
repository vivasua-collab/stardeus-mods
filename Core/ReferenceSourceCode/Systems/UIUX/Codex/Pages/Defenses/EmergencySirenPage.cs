using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class EmergencySirenPage : CodexConceptPage {
        public const string PageId = "Defenses/EmergencySiren";
        public override string Id => PageId;
        public override string TitleT => T.ObjectsDevicesEmergencysiren;
        protected override string GroupTitleT => T.CodexMDefenses;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutCombatStep1_3, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Defenses_EmergencySiren, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutCombatStep1_3);
        }
    }
}
