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
    public class AllSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new AllSearchAdapter());
        }

        private const string AdapterId = "All";
        public override string Id => AdapterId;
        public override string ResultLabel => T.All;
        public override int ResultOrder => -10000;
        public override bool ShowInResearch => true;
        public override bool ShowInStarmap => true;
        public override bool AggregateResults => true;
        public override int ResultCount {
            get {
                if (adapters == null) { return 0; }
                int cnt = 0;
                for (int i = adapters.Length - 1; i >= 0; i--) {
                    var ad = adapters[i];
                    cnt += ad.ResultCount;
                }
                return cnt;
            }
        }
        private BaseSearchAdapter[] adapters;

        protected override void Query(string input, string q, Regex wordStart) {
            if (adapters == null) {
                adapters = Registries.SearchAdapters.All.Values
                    .Where(a => a != this).OrderBy(
                        a => a.ResultOrder).ToArray();
            }
            // nop - this adapter will show results of all other adapters
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            // not pinnable
            return null;
        }

        public override void ForEachResult(Action<BaseSearchResult, string> act) {
            foreach (var ad in adapters) {
                // pass through results, but add a label
                ad.ForEachResult((res, _) => act(res, ad.ResultLabel));
            }
        }


        protected override void ResetAdapter() {
            adapters = null;
        }
    }
}
