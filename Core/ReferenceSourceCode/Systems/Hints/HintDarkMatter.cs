using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintDarkMatter : Hint {
        public const string Id = "dark_matter";
        protected override string id => Id;
        protected override bool IsRecurring => false;
        public override string Text => T.HintDarkMatterAcquisition_V2.F(
            Texts.LinkToCodex(DefIdsH.ObjMaterialsDarkMatter),
            Texts.LinkToResearch(DefIdsH.ResearchScanningExtended, T.SystemScanExtended));

        [SelfInit]
        private static void AutoRegister() {
            Register(new HintDarkMatter());
        }
    }
}
