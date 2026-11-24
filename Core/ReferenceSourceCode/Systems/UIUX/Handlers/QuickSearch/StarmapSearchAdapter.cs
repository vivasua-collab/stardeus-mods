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
    public class StarmapSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new StarmapSearchAdapter());
        }

        private const string AdapterId = "Starmap";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounObject;
        public override int ResultOrder => 10;

        public override bool ShowInMap => false;
        public override bool ShowInStarmap => true;

        protected override void Query(string input, string q, Regex wordStart) {

            var ctx = A.S.UIQuery.GetStarmapContext.Ask();
            var so = ctx.ParentSO;
            var res = Caches.SpaceObjectList;
            res.Clear();
            foreach (var child in so.Children) {
                if (child.Type.StarmapDecoration) {
                    continue;
                }
                res.Add(child);
            }

            var orderedDefs = res.OrderBy(o => o.Name);

            QueryItems(orderedDefs, q, wordStart,
                o => o.Id,
                o => Strings.ToSearchString(o.Name),
                o => o.DisplayName,
                o => o.Type.NameT,
                (o, idx) => new StarmapSearchResult(this, o, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            // these can't be pinned
            return null;
        }
    }
}
