using System;
using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class MainMenuCreateModsPanel : BaseMainMenuPanel {
        override protected string Id => "CreateMods";
        private WButtonText2 subButtonPrefab;
        public MainMenuCreateModsPanel(MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ctx.SubMenuPanel.Toggle(true);
        }

        private void AddURLButton(int idx, string title, string url) {
            var loadBtn = UISpawn.Widget(lifetime, subButtonPrefab, ctx.SubMenuPanel.Content.Container, idx);
            loadBtn.Load(Texts.ExternalLink(title), () => Files.OpenOSUrl(url),
                url.Replace("https://", string.Empty));
        }

        protected override void OnLoadContents() {
            subButtonPrefab ??= ResourceLibrary.FindPrefab<WButtonText2>(
                UIPrefabId.WMainMenuButtonSub);
            int idx = 0;

            var openDirBtn = UISpawn.Widget(lifetime, subButtonPrefab, ctx.SubMenuPanel.Content.Container, idx++);
            var dir = $"{Application.persistentDataPath}/Mods/";
            openDirBtn.Load(Texts.ExternalLink("Open User Mods Directory"),
                () => Files.OpenOSFolder(dir), "Place your mods here");
            openDirBtn.SetSiblingIdx(idx);

            AddURLButton(idx++, "Mod Template", "https://github.com/kodolinija/stardeus-mod-template");
            AddURLButton(idx++, "Modding Tutorials", "https://youtube.com/playlist?list=PLvm1mLYInibc8n0Q5_caRql5nEUiBwvTC");
            AddURLButton(idx++, "Modding EULA", "https://stardeusgame.com/eula/#modding-agreement");
        }

    }
}
