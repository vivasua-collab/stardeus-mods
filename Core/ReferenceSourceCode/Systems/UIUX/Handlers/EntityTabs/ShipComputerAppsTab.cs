using System;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Path;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ShipComputerAppsTab : BaseEntityTab {
        public const string TabId = "ShipComputerApps";

        [SelfInit]
        private static void Register() {
            RegisterTab(new ShipComputerAppsTab());
        }

        private Tile tile;
        private ShipComputerComp computer;

        public override string TabName => T.NounAppPlural;

        public override int Order => -10;

        public override bool ShowFor(GameState s, Tile tile) {
            if (tile.TryGetComponent(out computer)) {
                this.tile = tile;
                return true;
            }
            return false;
        }


        // A reveal wave added apps since the player last opened the Ship Computer: point at
        // the tab once, after the tab row has been laid out
        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            if (!A.S.UIQuery.TakeNewAppsHint.Ask()) { return; }
            Coroutines.OnNextFrame(() => {
                if (widget != null && widget.gameObject.activeInHierarchy) {
                    UIFocusRing.Request(widget.Rect);
                }
            });
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Widget<WContentBlock>(lifetime, UIPrefabId.WContentBlock_Grid, group.Container, 0);
            var idx = 0;

            // Only apps the player can use right now: research-locked ones and ones with
            // nothing to act on stay out, and during a new run's tutorial the HUD reveal
            // adds the rest as the chain reaches them (HudRevealRules)
            var s = A.S;
            var apps = Registries.ShipComputerApps.All.Values
                .Where(a => a.IsVisible && a.IsAvailable
                    && s.UIQuery.IsHudRevealed.Ask(HudRevealRules.AppKey(a.Id)))
                .OrderBy(a => a.Name);

            foreach (var app in apps) {
                var appButton = UISpawn.Widget<WAppButton>(lifetime, UIPrefabId.WAppButton, block.Container, idx++);
                appButton.IconBG.SetTint(The.UITheme.GetColor(app.ColorId, this));
                appButton.Button.Load(app.Icon, app.Name, app.OnClick);
                appButton.ShortcutGlyph.SetActiveFast(false);
                if (!app.IsAvailable) {
                    appButton.Button.SetInteractable(false);
                    appButton.Button.SetText(Texts.Grey(app.Name));
                    if (app.RequiredResearch != null) {
                        UIUtils.AddTip(lifetime, appButton,
                            $"{T.ResearchNodeLocked}: {Texts.LinkToResearch(app.RequiredResearch)}");
                    }
                } else {
                    var glyph = app.Shortcut != ActionType.None
                        ? The.Bindings.GetGlyphFor(app.Shortcut)
                        : ShipComputerAppHotkeys.GlyphFor(app);
                    if (!string.IsNullOrEmpty(glyph)) {
                        appButton.ShortcutGlyph.Load(glyph);
                        appButton.ShortcutGlyph.SetActiveFast(true);
                    }
                }
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            // Footer hack allows the text to be double width in the entity panel footer
            // where grid items are forced to be 210x40 only
            var text = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WTextCentered_EntityPanelFooterHack, group.Container, 0);
            text.Load(Texts.Italic($"Powered by {A.S.ShipComputerName} {Consts.Version}"));
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            computer = null;
        }

        public override string Id => TabId;
    }
}
