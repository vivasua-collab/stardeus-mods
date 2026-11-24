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
    public class OpenUISearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new OpenUISearchAdapter());
        }
        public const string AdapterId = "OpenUI";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounUserInterface;
        public override int ResultOrder => 0;

        protected override void Query(string input, string q, Regex wordStart) {
            var orderedCmds = Registries.SearchCommands.All.Values.Where(
                c => c.AdapterId == AdapterId && c.IsEnabled)
                    .OrderBy(c => c.TitleT);

            var displayed = Caches.IntSet;
            displayed.Clear();

            QueryItems(orderedCmds, q, wordStart,
                c => c.IdH,
                c => c.SearchNameT,
                c => c.TitleT,
                c => c.DescT,
                (c, idx) => new ExecuteCommandSearchResult(this, c, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            var cmd = Registries.SearchCommands.Get(resultId);
            if (cmd != null) {
                return new ExecuteCommandSearchResult(this, cmd, order);
            }
            return null;
        }
    }
}
