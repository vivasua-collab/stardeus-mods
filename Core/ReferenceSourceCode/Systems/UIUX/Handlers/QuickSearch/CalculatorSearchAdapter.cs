using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Utils;
using KL.Utils.Calc;

namespace Game.Systems.UIUX {
    public class CalculatorSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new CalculatorSearchAdapter());
        }
        public const string AdapterId = "Calculator";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounCalculator;
        public override int ResultOrder => -10000;

        protected override void Query(string input, string q, Regex wordStart) {
            Results.Clear();
            try {
                if (!StringCalc.IsMathematicalEquation(input)) {
                    return;
                }
                float result = StringCalc.Calculate(input);
                Results.Add(new CalculatorSearchResult(this, input, result, 0));
            } catch {}
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            return null;
        }
    }
}
