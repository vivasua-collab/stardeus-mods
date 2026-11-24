using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class BuildTileSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new BuildTileSearchAdapter());
        }

        private const string AdapterId = "BuildTile";
        public override string Id => AdapterId;
        public override string ResultLabel => T.AbilityBuild;
        public override int ResultOrder => 10;

        protected override void Query(string input, string q, Regex wordStart) {
            var res = A.S.Research;
            var orderedDefs = The.Defs.Defs.Values.Where(
                v => (!v.SkipToolListing || A.InstantBuild)
                    && !v.IsTemplate && !v.IsAbstract
                    && res.IsDefUnlocked(v)
                    && WorldLayer.IsDataTileLayer(v.LayerId))
                        .OrderBy(d => d.NameT);

            QueryItems(orderedDefs, q, wordStart,
                d => d.IdH,
                d => d.SearchNameT,
                d => d.NameT,
                d => d.DescT,
                (d, idx) => new BuildTileSearchResult(this, d, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            var def = The.Defs.Get(resultId);
            if (def != null) {
                return new BuildTileSearchResult(this, def, order);
            }
            return null;
        }
    }
}
