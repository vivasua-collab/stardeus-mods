using System;
using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class MainMenuAboutPanel : BaseMainMenuPanel {
        override protected string Id => "About";
        private WButtonText2 subButtonPrefab;
        public MainMenuAboutPanel(MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ctx.SubMenuPanel.Toggle(true);
        }

        private void AddURLButton(int idx, string title, string url) {
            var loadBtn = UISpawn.Widget(lifetime, subButtonPrefab, ctx.SubMenuPanel.Content.Container, idx);
            loadBtn.Load(Texts.ExternalLink(title), () => Files.OpenOSUrl(url),
                url.Replace("https://", string.Empty));
        }

        protected override void OnLoadContents() {
            int idx = 0;
            subButtonPrefab ??= ResourceLibrary.FindPrefab<WButtonText2>(
                UIPrefabId.WMainMenuButtonSub);

            AddURLButton(idx++, T.GameWebsite, "https://stardeusgame.com");
            #if STARDEUS_GOG
            AddURLButton(idx++, "GOG", "https://gog.com/en/game/stardeus");
            #else
            AddURLButton(idx++, T.GameSteamPage, "https://store.steampowered.com/app/1380910/Stardeus/");
            #endif
            AddURLButton(idx++, T.GameDiscord, "https://discordapp.com/invite/89amEwP");
            #if !STARDEUS_GOG
            AddURLButton(idx++, T.GameForum, "https://steamcommunity.com/app/1380910/discussions/");
            #endif
            AddURLButton(idx++, T.GameGraphs, "https://kodolinija.github.io/stardeus-graphs/");
            AddURLButton(idx++, T.StudioWebsite, "https://kodolinija.com");
            AddURLButton(idx++, T.GameReddit, "https://reddit.com/r/stardeus");
            AddURLButton(idx++, T.GameTwitter, "https://twitter.com/StardeusGame");
            AddURLButton(idx++, T.GamePresskit, "https://presskit.kodolinija.com/stardeus/");
            AddURLButton(idx++, T.PrivacyPolicy, "https://stardeusgame.com/privacy/");
            AddURLButton(idx++, T.UnityDataPrivacy, "https://unity3d.com/legal/privacy-policy");
            AddURLButton(idx++, "EULA", "https://stardeusgame.com/eula/");
        }

    }
}
