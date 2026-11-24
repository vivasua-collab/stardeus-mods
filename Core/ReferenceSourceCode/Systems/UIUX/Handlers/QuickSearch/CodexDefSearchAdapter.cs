using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class CodexDefSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new CodexDefSearchAdapter());
        }

        private const string AdapterId = "CodexDef";
        public override string Id => AdapterId;
        public override string ResultLabel => T.Codex;
        // last
        public override int ResultOrder => 10000;

        protected override void Query(string input, string q, Regex wordStart) {
            var research = A.S.Research;
            var orderedDefs = The.Defs.Defs.Values.Where(
                d => !d.IsAbstract && !d.IsTemplate && d.Researchable?.IsTreeNode != true
                    && (Researchable.IsEmptyPrereq(d)
                        || research.IsDiscovered(Hashes.S(d.Researchable.Prerequisites[0]))))
                .OrderBy(v => v.NameT);

            QueryItems(orderedDefs, q, wordStart,
                d => d.IdH,
                d => d.SearchNameT,
                d => d.NameT,
                d => d.DescT,
                (d, idx) => new CodexDefSearchResult(this, d, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            // these can't be pinned
            return null;
        }
    }
}
