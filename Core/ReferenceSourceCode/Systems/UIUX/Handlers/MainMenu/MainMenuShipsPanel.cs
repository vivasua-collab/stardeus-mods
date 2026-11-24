using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class MainMenuShipsPanel : BaseMainMenuPanel {
        override protected string Id => "Ships";

        public MainMenuShipsPanel(MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ctx.SubMenuPanel.Toggle(true);
        }

        protected override void OnLoadContents() {
            int idx = 0;
            var sharedSaves = The.ModLoader.Discovery.SharedSaves;
            foreach (var path in sharedSaves) {
                var saveInfoPath = $"{path}/SaveInfo.json";
                var json = Files.ReadText(saveInfoPath);
                if (string.IsNullOrEmpty(json)) { continue; }
                var info = JsonUtility.FromJson<SharedSaveInfo>(json);
                var loadBtn = UISpawn.Widget<WButtonText2>(lifetime,
                    UIPrefabId.WMainMenuButtonSub, ctx.SubMenuPanel.Content.Container, idx++);
                RigShipButton(loadBtn, info, path);
            }
        }

        private void RigShipButton(WButtonText2 loadBtn, SharedSaveInfo info, string path) {
            loadBtn.SetText(info.Title);
            loadBtn.SetText2(info.Creator);
            loadBtn.SetOnClick(() => ShowShip(info, path));
        }

        private void ShowShip(SharedSaveInfo info, string path) {
            ctx.ModInfoWidget.LoadShip(info, path);
            ctx.ModInfoWidget.Toggle(true);
        }
    }
}
