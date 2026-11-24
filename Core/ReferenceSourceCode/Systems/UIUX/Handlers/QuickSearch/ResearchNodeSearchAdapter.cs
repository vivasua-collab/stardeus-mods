using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Collections;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class ResearchNodeSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new ResearchNodeSearchAdapter());
        }

        private const string AdapterId = "ResearchNode";
        public override string Id => AdapterId;
        public override string ResultLabel => T.OverlayTipResearch;
        public override int ResultOrder => 10;

        public override bool ShowInMap => true;
        public override bool ShowInResearch => true;

        protected override void Query(string input, string q, Regex wordStart) {
            var research = A.S.Research;
            var nodes = ResearchTree.All
                .Where(n => research.IsDiscovered(n.IdH))
                .OrderBy(n => n.NameT);

            QueryItems(nodes, q, wordStart,
                d => d.IdH,
                d => Strings.ToSearchString(d.SearchNameT),
                d => d.NameT,
                null,
                (d, idx) => new ResearchNodeSearchResult(this, d, idx));
        }


        public override BaseSearchResult LoadResult(string resultId, int order) {
            // these can't be pinned
            return null;
        }
    }
}
