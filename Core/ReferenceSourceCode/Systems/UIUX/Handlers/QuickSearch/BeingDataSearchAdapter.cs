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
    public class BeingDataSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new BeingDataSearchAdapter());
        }

        private const string AdapterId = "BeingData";
        public override string Id => AdapterId;
        public override string ResultLabel => T.Beings;
        public override int ResultOrder => 20;

        protected override void Query(string input, string q, Regex wordStart) {
            var res = A.S.Research;
            var orderedDefs = The.Defs.Defs.Values.Where(
                v => (!v.SkipToolListing || A.InstantBuild)
                    && !v.IsTemplate && !v.IsAbstract
                    && res.IsDefUnlocked(v)
                    && WorldLayer.IsDataTileLayer(v.LayerId))
                        .OrderBy(d => d.NameT);

            var allBeings = A.S.Query.AllBeingData.Ask().Values;
            var orderedBeings = allBeings.Where(
                b => b.IsPlayer && !b.Body.HasFlag(BodyParts.BodyFlags.Missing))
                    .OrderBy(b => b.Name);

            QueryItems(orderedBeings, q, wordStart,
                d => (int) d.Id,
                d => d.SearchString,
                d => d.Name,
                d => d.UI.GetBio(false, true),
                (d, idx) => new BeingDataSearchResult(this, d, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            if (uint.TryParse(resultId, out uint beingId)) {
                var being = A.S.Query.FindBeingData.Ask(beingId);
                if (being != null) {
                    return new BeingDataSearchResult(this, being, order);
                }
            }
            return null;
        }
    }
}
