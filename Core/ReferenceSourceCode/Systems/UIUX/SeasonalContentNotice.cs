using System;
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    // One-time "this seasonal content is available" popup, shown after the first save
    // load of the running app instance (new game or load game). Never shown again in the
    // same session, even across further loads. See Docs/Architecture/SeasonalContent.md.
    public static class SeasonalContentNotice {
        private const string hiddenPrefPrefix = "seasonal.notice.hidden";

        [ClearOnReload]
        private static bool hasShown;

        [SelfInit]
        private static void Register() {
            The.SysSig.AfterLoadSaveState.AddListener(OnLoaded);
        }

        private static void OnLoaded(GameState s) {
            if (hasShown) { return; }
            Ready.WhenGame(typeof(SeasonalContentNotice), "SeasonalContentNotice", TryShow);
        }

        private static void TryShow() {
            if (hasShown) { return; }
            var active = new List<Def>();
            var inSeason = new List<int>();
            foreach (var def in The.Defs.Defs.Values) {
                if (def.Seasonal == null || !def.IsInSeason) { continue; }
                inSeason.Add(def.IdH);
                if (Researchable.TreeNode(def)) {
                    active.Add(def);
                }
            }
            if (active.Count == 0) { return; }
            var hiddenKey = HiddenPrefKey(inSeason);
            if (The.Prefs.GetBoolAutoHash(hiddenKey, false)) { return; }
            hasShown = true;
            active.Sort(Def.NameComparer);
            var sb = new StringBuilder();
            foreach (var def in active) {
                sb.AppendFormat("{0} ({1})\n",
                    Texts.LinkToResearch(def.IdH, Texts.LinkColor(def.NameT)),
                    def.Seasonal.RangeText);
            }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) { return; }
            #endif
            WPopup.Create(false, IconId.CParty,
                T.SeasonalContentAvailable, T.SeasonalContentAvailableDesc,
                sb.ToString())
                .WithAction(T.Ok, null)
                .WithAction2(T.DoNotShowAgain, () => The.Prefs.SetBool(hiddenKey, true))
                .WithCloseOnLinkClick()
                .Spawn(center: true);
        }

        // Dismissal holds for this year and this exact set of seasonal defs only, so
        // next year's season or newly added seasonal content shows the popup again
        private static string HiddenPrefKey(List<int> inSeason) {
            inSeason.Sort();
            var setHash = 17;
            for (int i = 0; i < inSeason.Count; i++) {
                setHash = Hashes.Quick(setHash, (uint) inSeason[i]);
            }
            return $"{hiddenPrefPrefix}.{DateTime.Now.Year}.{setHash}";
        }
    }
}
