using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Atlas;
using KL.Collections;
using KL.Grid;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class EntityPanelMultiWidget : BaseWidget, ICloseableWidget {
        // Selections can hold up to Consts.MaxSelectedEntities entities. Spawning a
        // row widget for each of them freezes the game, so rows are revealed in pages.
        private const int PageSize = 25;
        [SerializeField] private WButton wPanelHeader;
        [SerializeField] private WText wHeaderText;
        [SerializeField] private WGroup wItems;
        protected override bool IsPoolable => false;
        private Lifetime lifetime;
        public bool IsShowing;
        private double lastClickTime;
        private ScrollRect scroll;
        public void Initialize() {
            this.SetActiveFast(false);
            Register(UIWidgetId.EntityPanelMulti, this);
            // Widget is already inactive here, so inactive parents must be included
            scroll = wItems.GetComponentInParent<ScrollRect>(true);
            MiscUtils.DestroyChildren(wItems.Container);
            wPanelHeader.SetOnClick(() => {
                Close();
                A.S.Query.DeselectAny.Ask();
            });
        }

        public bool ShowBeings(GameState s, List<Being> beings) {
            Cleanup();
            CreateLifetime();
            LoadBeings(s, beings);
            Show();
            return true;
        }

        private void CreateLifetime() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("EntityPanelMulti");
        }

        public bool ShowObjs(GameState s, List<Obj> objs) {
            Cleanup();
            CreateLifetime();
            LoadObjs(s, objs);
            Show();
            return true;
        }

        public bool ShowTiles(GameState s, List<Tile> tiles) {
            Cleanup();
            CreateLifetime();
            LoadTiles(s, tiles);
            Show();
            return true;
        }

        private void Show() {
            if (IsShowing) { return; }
            UIShowing.Set(UIShowingFlags.EntityPanel);
            IsShowing = true;
            Rect.SetAsLastSibling();
            this.SetActiveFast(true);
        }

        private void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        private void LoadBeings(GameState s, List<Being> beings) {
            wHeaderText.SetText($"{Units.XNum(beings.Count)} {T.Beings}");
            var species = beings[0].Species;
            foreach (var b in beings) {
                if (b.Species != species) {
                    species = null;
                    wHeaderText.SetText($"{T.Beings} {Units.XNum(beings.Count)}");
                    break;
                }
            }
            if (species != null) {
                wHeaderText.SetText($"{species.Name} {Units.XNum(beings.Count)}");
            }
            var block = UISpawn.Block(lifetime, wItems, 0);
            var ordered = beings.OrderBy(b => b.Data.Mind.Name.FullNameOrNameless).ToList();
            SpawnPaged(ordered, block.Container, (being, idx) => {
                var w = UISpawn.Widget<WBeingListItem>(lifetime,
                    UIPrefabId.WBeingListItem, block.Container, idx);
                w.Load(being.Data, () => {
                    FocusOrSelectOnDblClick(being);
                });
                UIUtils.AddCodexTooltip(lifetime, w.gameObject, w, being.Definition);
                UIUtils.AddHover(lifetime, w, (h) => {
                    EntityUtils.ShowEntityLink(null, being, h);
                });
            });
        }

        /// <summary>
        /// Spawns the first page of rows and a "+N" button that reveals the next page.
        /// spawnAt receives the item and the sibling index its row must take.
        /// </summary>
        private void SpawnPaged<T>(List<T> ordered, RectTransform container,
                Action<T, int> spawnAt) {
            int loadCount = Math.Min(PageSize, ordered.Count);
            for (int i = 0; i < loadCount; i++) {
                spawnAt(ordered[i], i);
            }
            if (ordered.Count > PageSize) {
                var btn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WContent2ButtonSimple, container, loadCount);
                LoadMoreAction(btn, ordered, spawnAt, loadCount);
            }
        }

        private void LoadMoreAction<T>(WButtonText btn, List<T> ordered,
                Action<T, int> spawnAt, int loadedCount) {
            btn.Load(Texts.Italic($"+{ordered.Count - loadedCount}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(PageSize, ordered.Count - loadedCount);
                for (int i = 0; i < batch; i++) {
                    spawnAt(ordered[loadedCount + i], sibIdx + i);
                }
                int newLoaded = loadedCount + batch;
                if (newLoaded < ordered.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreAction(btn, ordered, spawnAt, newLoaded);
                } else {
                    btn.gameObject.SetActiveFast(false);
                }
                RebuildScroll();
            });
        }

        // Rebuilds without touching the scroll position, so the player stays where
        // they are while the new rows appear below
        private void RebuildScroll() {
            if (scroll == null) { return; }
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
        }

        private void FocusOrSelectOnDblClick(Entity e) {
            var t = GameTimer.realtimeSinceStartupAsDouble;
            if (t - lastClickTime < Consts.DoubleClickTime) {
                EntityUtils.SelectOrFocus(e);
            } else {
                EntityUtils.CameraFocusOn(e, false);
                lastClickTime = t;
            }
        }

        private void LoadObjs(GameState s, List<Obj> objs) {
            Def def = objs[0].Definition;
            int pileCount = 0;
            foreach (var obj in objs) {
                if (obj.Definition != def) {
                    def = null;
                    wHeaderText.SetText($"{T.Objects} {Units.XNum(objs.Count)}");
                    break;
                } else if (obj.Pile != null) {
                    pileCount += obj.Pile.StackSize;
                }
            }
            if (def != null) {
                if (pileCount > 1) {
                    wHeaderText.SetText($"{def.NameT} x{pileCount}");
                } else {
                    wHeaderText.SetText($"{def.NameT} {Units.XNum(objs.Count)}");
                }
            }
            var block = UISpawn.Block(lifetime, wItems, 0);
            var ordered = objs.OrderBy(o => o.Definition.NameT).ToList();
            SpawnPaged(ordered, block.Container, (obj, idx) => {
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx);
                w.Load(obj.Definition.Preview, ListNameFor(obj),
                    Texts.Grey(Pos.String(obj.PosIdx)), () => {
                    FocusOrSelectOnDblClick(obj);
                });
                obj.Data.ListNameChangedB.Bind(
                    lifetime, () => w.SetText(ListNameFor(obj)),
                    invokeImmediately: false);

                UIUtils.AddCodexTooltip(lifetime, w.gameObject, w, obj.Definition);
                UIUtils.AddHover(lifetime, w, (h) => {
                    EntityUtils.ShowEntityLink(null, obj, h);
                });
            });
        }

        private static string ListNameFor(Obj obj) {
            if (obj.Pile != null && obj.Pile.StackSize > 1) {
                return $"{obj.Definition.NameT} x{obj.Pile.StackSize}";
            }
            // Same markers as storage rows, so dead's and used gear can be told apart here
            return ItemOwnership.DecoratedItemName(obj.Data, null, obj.Data.ListName);
        }

        private void LoadTiles(GameState s, List<Tile> tiles) {
            Def def = tiles[0].Definition;
            foreach (var tile in tiles) {
                if (tile.Definition != def) {
                    def = null;
                    wHeaderText.SetText($"{T.MenuDevices} {Units.XNum(tiles.Count)}");
                    break;
                }
            }
            if (def != null) {
                wHeaderText.SetText($"{def.NameT} {Units.XNum(tiles.Count)}");
            }
            var block = UISpawn.Block(lifetime, wItems, 0);
            var ordered = tiles.OrderBy(t => t.Definition.NameT).ToList();
            SpawnPaged(ordered, block.Container, (tile, idx) => {
                var t = EntityUtils.ParentObj(tile);
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx);
                var upgrades = t.GetComponent<UpgradesComp>();
                w.Load(t.Definition.Preview, t.Definition.NameT, TileText2(t, upgrades), () => {
                    FocusOrSelectOnDblClick(t);
                });
                if (upgrades != null) {
                    upgrades.UpgradesChanged.Bind(lifetime,
                        () => w.SetText2(TileText2(t, upgrades)), invokeImmediately: false);
                }
                UIUtils.AddCodexTooltip(lifetime, w.gameObject, w, t.Definition);
                UIUtils.AddHover(lifetime, w, (h) => {
                    EntityUtils.ShowEntityLink(null, t, h);
                });
            });
        }

        // Installed upgrades go before the position, out of the name: they help pick
        // devices for a mass install without reading like part of what the device is
        private static string TileText2(Tile t, UpgradesComp upgrades) {
            var pos = Texts.Grey(Pos.String(t.PosIdx));
            var installed = upgrades?.Upgrades;
            if (installed == null || upgrades.UsedSlots == 0) { return pos; }
            var sb = Caches.StringBuilder;
            sb.Clear();
            foreach (var id in installed) {
                if (id == null) { continue; }
                var upgrade = UpgradeRegistry.Get(id);
                if (upgrade == null) { continue; }
                sb.Append(DefIcons.Tag(MatType.Get(upgrade.MatTypeId)?.Def));
            }
            if (sb.Length == 0) { return pos; }
            sb.Append(' ');
            sb.Append(pos);
            return sb.ToString();
        }

        public void Close() {
            if (!IsShowing) { return; }
            D.Log("Closing Entity Panel");
            UIShowing.Unset(UIShowingFlags.EntityPanel);
            Cleanup();
            IsShowing = false;
            this.SetActiveFast(false);
        }

        public bool TryCloseWidget() {
            Close();
            A.S.Query.DeselectAny.Ask();
            return true;
        }
    }
}
