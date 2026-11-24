using System;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Synthesized group header for a Codex section (e.g. "ShipCombat"). Created
    /// internally the first time a page registers into that group - never
    /// subclassed or self-registered directly.
    /// </summary>
    public sealed class CodexConceptGroup : CodexConceptPage {
        private readonly string id;

        public CodexConceptGroup(string id, Func<string> titleFunc) {
            this.id = id;
            this.titleFunc = titleFunc;
            SetGroupIds(id, id);
        }

        public override string Id => id;
        public override bool IsGroup => true;
        private readonly Func<string> titleFunc;
        protected override string GroupTitleT => titleFunc();
        public override string TitleT => titleFunc();

        public override void Build(GameState s, Lifetime life, WGroup group, int idx) {
            // Groups have no content of their own - only their items do.
        }

        public override bool OnSearchPageBody(string query) => false;
    }
}
