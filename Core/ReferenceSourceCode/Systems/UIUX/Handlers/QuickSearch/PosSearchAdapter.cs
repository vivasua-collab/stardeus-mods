using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using KL.Utils.Calc;

namespace Game.Systems.UIUX {
    public class PosSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new PosSearchAdapter());
        }
        public const string AdapterId = "Pos";
        public override string Id => AdapterId;
        public override string ResultLabel => T.Position;
        public override int ResultOrder => -10000;

        protected override void Query(string input, string q, Regex wordStart) {
            Results.Clear();
            if (!input.Contains(':') && !input.Contains(' ')) {
                return;
            }
            try {
                if (!Pos.TryParse(input, out var idx)) {
                    return;
                }
                Results.Add(new PosSearchResult(this, idx, 0));
            } catch {}
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            return null;
        }
    }
}
