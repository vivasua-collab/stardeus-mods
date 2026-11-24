#if PRODUCTION_BUILD
#define AUTO_REFRESH_SAVES
#endif
#define AUTO_REFRESH_SAVES
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class MainMenuLoadPanel : BaseMainMenuPanel {
        override protected string Id => "Load";
        public MainMenuLoadPanel(MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ctx.LoadGameWidget.OnDeleteSave = OnDeleteSave;
            ctx.SubMenuPanel.Toggle(true);
        }

        private void OnDeleteSave() {
            RefreshSaves();
        }

        protected override void OnLoadContents() {
            int idx = 0;
            #if AUTO_REFRESH_SAVES
            SaveLoadUtils.ClearCache();
            #endif
            GameStateMeta first = null;
            foreach (var game in SaveLoadUtils.SavesByGame) {
                var loadBtn = UISpawn.Widget<WButtonText2>(lifetime,
                    UIPrefabId.WMainMenuButtonSub, ctx.SubMenuPanel.Content.Container, idx++);
                RigLoadButton(loadBtn, game);
                if (first == null && !Arrays.IsEmpty(game.Value)) {
                    first = game.Value.First();
                }
            }
            #if !AUTO_REFRESH_SAVES
            var refreshBtn = UISpawn.Widget<WButtonText>(
                    UIPrefabId.WMenuPanelButton, ctx.SubMenuPanel.Content.Container);
            refreshBtn.Load(T.Refresh, RefreshSaves);
            refreshBtn.SetSiblingIdx(idx++);
            #endif
            if (first != null) {
                LoadSave(first);
            }
        }

        private void RefreshSaves() {
            RefreshSaves(clearCache: true);
        }
        private void RefreshSaves(bool clearCache) {
            if (clearCache) {
                SaveLoadUtils.ClearCache();
            }
            LoadContents();
            var scroll = ctx.SubMenuPanel.GetComponentInChildren<ScrollRect>();
            if (scroll != null) {
                scroll.verticalNormalizedPosition = 1f;
            }
        }

        private void RigLoadButton(WButtonText2 loadBtn,
                KeyValuePair<string, List<GameStateMeta>> game) {
            var meta = game.Value[0];
            var saveName = SaveLoadUtils.SaveName(meta, out var subText);
            var saveVersionText = SaveLoadUtils.SaveVersionText(meta);
            if (saveVersionText != null) {
                subText = $"{subText} / {saveVersionText}";
            }
            #if !PRODUCTION_BUILD
            if (game.Value.Count > 1)  {
                saveName += Texts.GreySmaller($" ({Units.XNum(game.Value.Count)})");
            }
            #endif
            loadBtn.SetText(saveName);
            loadBtn.SetText2(subText);
            loadBtn.SetOnClick(() => LoadSave(meta));
            Tooltip.CreateFor(lifetime, ctx.SubMenuPanel, loadBtn.Rect, () => SaveLoadUtils.SaveTooltip(meta));
        }

        private void LoadSave(GameStateMeta meta) {
            ctx.LoadGameWidget.Load(meta);
            ctx.LoadGameWidget.Toggle(true);
        }
    }
}
