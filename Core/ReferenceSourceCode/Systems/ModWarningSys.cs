using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.Utils;
using UnityEngine;
using Game.UI;

#if !DISABLESTEAMWORKS
using Game.Platform.Steam;

using KL.Utils;
#endif

namespace Game.Systems {
    // Warns the player once per app session that third-party mods are enabled,
    // since most "game breaking bug" / balance reports turn out to be mod-caused.
    public sealed class ModWarningSys : GameSystem {
        public const string SysId = "ModWarningSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ModWarningSys());
        }

        private static bool shownThisSession;
        private const string WorkshopBrowseUrl =
            "https://steamcommunity.com/my/myworkshopfiles?browsefilter=mysubscriptions&sortmethod=subscriptiondate&section=items&appid=1380910&requiredtags%5B%5D=Mod";

        protected override void OnInitialize() {
            S.Sig.AfterLoad.AddListener(OnGameLoaded);
        }

        public override void Unload() {
            // shownThisSession is intentionally static/per-app-session, not reset here.
        }

        private void OnGameLoaded() {
            if (shownThisSession) { return; }
            var mods = The.ModLoader.LoadedMods
                .Where(m => m.Id != "Core" && m.Id != "Translations" && !m.IsCoreTranslation)
                .OrderBy(m => m.Name)
                .ToList();
            if (mods.Count == 0) { return; }
            shownThisSession = true;

            var glyph = The.Bindings.GetGlyphFor(ActionType.ToggleConsole);
            var consoleHint = string.IsNullOrWhiteSpace(glyph) ? string.Empty : $"({glyph})";

            var sb = new StringBuilder();
            sb.Append(T.ModsActiveWarningBody_V1.F(consoleHint));
            foreach (var mod in mods) {
                sb.AppendFormat("\n- {0}", ModLine(mod));
            }

            var popup = WPopup.Create(true, IconId.CWrench2,
                    T.ModsActiveWarningTitle, null, sb.ToString());
            #if !DISABLESTEAMWORKS
            popup.WithAction2(T.Ok, null);
            popup.WithAction(Texts.ExternalLink(T.NounSubscribedMods),
                () => Files.OpenOSUrl(WorkshopBrowseUrl));
            #else
            popup.WithAction(T.Ok, null);
            #endif
            popup.Spawn(UIPoolRoot.SystemPopupContainer);
        }

        private static string ModLine(ModInfo mod) {
            var name = $"{mod.Name} [{mod.ModVersion}]";
            #if !DISABLESTEAMWORKS
            if (mod.SteamWorkshopId != 0) {
                return Texts.HTTPLink(SteamWorkshopItem.URLFor(mod), name);
            }
            #endif
            return name;
        }
    }
}
