#if PRODUCTION_BUILD
#define AUTO_REFRESH_SAVES
#endif
#define AUTO_REFRESH_SAVES
using System;
using System.CodeDom;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Platform;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class MainMenuModsPanel : BaseMainMenuPanel {
        override protected string Id => "Mods";
        private Lifetime contentLife;

        public MainMenuModsPanel(MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ctx.SubMenuPanel.Toggle(true);
        }

        protected override void OnLoadContents() {
            Rebuild();
        }

        private void Rebuild() {
            contentLife?.Dispose();
            contentLife = lifetime.CreateChild("ModsContent");
            int idx = 0;
            var canEnableAny = false;
            var canDisableAny = false;
            foreach (var mod in The.ModLoader.ModInfos.Values) {
                var loadBtn = UISpawn.Widget<WButtonText2WithToggle>(contentLife,
                    UIPrefabId.WMainMenuButtonSub_Toggle, ctx.SubMenuPanel.Content.Container, idx++);
                RigModButton(loadBtn, mod);
                if (mod.Id == "Core") { continue; }
                if (ModLoader.IsModEnabled(mod.Id)) { canDisableAny = true; }
                else if (!mod.IsDevDisabled) { canEnableAny = true; }
            }
            BuildFooter(canEnableAny, canDisableAny);
        }

        private void BuildFooter(bool canEnableAny, bool canDisableAny) {
            if (!canEnableAny && !canDisableAny) {
                ctx.SubMenuPanel.ShowFooter(false);
                return;
            }
            var b = UISpawn.Widget<WButtonIconText2>(contentLife,
                UIPrefabId.WContent2Button, ctx.SubMenuPanel.Footer.Container, 0);
            b.Load(IconId.CCog, T.Manage, null, () => {
                var qc = QuickChoice.Create();
                qc.Widget = b.Rect;
                if (canDisableAny) { qc.AddButton(T.DisableAllMods, () => SetAllMods(false)); }
                if (canEnableAny) { qc.AddButton(T.EnableAllMods, () => SetAllMods(true)); }
                qc.Show();
            });
            ctx.SubMenuPanel.ShowFooter(true);
        }

        private void SetAllMods(bool enabled) {
            foreach (var mod in The.ModLoader.ModInfos.Values) {
                if (mod.Id == "Core") { continue; }
                if (enabled && mod.IsDevDisabled) { continue; }
                ModLoader.ToggleMod(mod.Id, enabled);
            }
            Rebuild();
            PlatformUtils.SuggestRestart();
        }

        private void RigModButton(WButtonText2WithToggle loadBtn, ModInfo mod) {
            loadBtn.Button.SetText(mod.NameDecorated);
            loadBtn.Button.SetText2(mod.ModVersion);
            loadBtn.Button.SetOnClick(() => ShowMod(mod));
            loadBtn.Toggle.Load(ModLoader.IsModEnabled(mod.Id), (v) => ToggleMod(mod, v));
            if (mod.IsDevDisabled) {
                loadBtn.Toggle.SetCheckmarkOverride(IconId.WClose, The.UITheme.GetColor("Red", this));
            }
            if (mod.Id == "Core") {
                loadBtn.Toggle.SetInteractable(false);
            }

            // loadBtn.SetText(saveName);
            // loadBtn.SetText2(subText);
            // loadBtn.SetOnClick(() => LoadSave(meta));
            // Tooltip.CreateFor(ctx.SubMenuPanel, loadBtn.Rect, () => SaveLoadUtils.SaveTooltip(meta));
        }

        private void ToggleMod(ModInfo mod, bool v) {
            if (mod.IsDevDisabled) {
                WPopup.Create(true, IconId.CCross, T.OperationFailed,
                    "This mod is broken or the version is marked as obsolete by the developer, it cannot be enabled.\n\nIt is either broken or not compatible with core game mechanics anymore.\n\nIf you are the developer of this mod, increment the <b>ModVersion</b> ({info.ModVersion}) in <b>ModInfo.json</b> and the mod will no longer be automatically disabled.")
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            ModLoader.ToggleMod(mod.Id, v);
            Rebuild();
            ShowMod(mod);
            PlatformUtils.SuggestRestart();
        }

        private void ShowMod(ModInfo mod) {
            ctx.ModInfoWidget.Load(mod);
            ctx.ModInfoWidget.Toggle(true);
        }
    }
}
