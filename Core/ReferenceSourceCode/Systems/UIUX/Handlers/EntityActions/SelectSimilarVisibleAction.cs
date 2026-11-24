using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SelectSimilarVisibleAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new SelectSimilarVisibleAction());
        }

        public const string ActionId = "SelectSimilarVisible";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.SelectSimilarInView;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!HasSimilarBeing(s, entity)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, () => SelectSimilarVisibleBeings(s, entity)),
                Priority.Low, 2);
            return true;
        }

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!HasSimilarObj(s, obj)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, () => SelectSimilarVisibleObjs(s, obj)),
                Priority.Low, 2);
            return true;
        }

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            return ApplyForTile(s, tile, out data);
        }

        private bool ApplyForTile(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!HasSimilarTile(s, tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, () => SelectSimilarVisibleTiles(s, tile)),
                Priority.Low, 2);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, Action onClick) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CSelectSimilar, T.SelectSimilarInView, onClick);
            UIUtils.AddTip(l, widget, T.SelectSimilarInViewTip);
            return widget;
        }

        private static bool HasSimilarBeing(GameState s, Being source) {
            if (!source.IsActive) {
                return false;
            }

            var species = source.Species;
            var isAlive = source.IsAlive;
            int count = 0;
            foreach (var being in s.Beings.All) {
                if (!being.IsActive) {
                    continue;
                }
                if (being.Species == species && being.IsAlive == isAlive) {
                    count++;
                    if (count > 1) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasSimilarObj(GameState s, Obj source) {
            if (!source.IsActive) {
                return false;
            }

            var def = source.Definition;
            int count = 0;
            foreach (var obj in s.Objs.All) {
                if (!obj.IsActive) {
                    continue;
                }
                if (obj.Definition == def) {
                    count++;
                    if (count > 1) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasSimilarTile(GameState s, Tile source) {
            if (!source.IsActive) {
                return false;
            }

            var def = source.Definition;
            var isConstructed = source.IsConstructed;
            int count = 0;
            foreach (var entity in s.Grids[def.LayerId].Data) {
                if (entity?.IsActive != true) {
                    continue;
                }
                if (entity.Definition == def && entity.IsConstructed == isConstructed) {
                    count++;
                    if (count > 1) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void SelectSimilarVisibleBeings(GameState s, Being source) {
            if (!source.IsActive) {
                return;
            }

            s.Query.DeselectAny.Ask();
            var selectedBeings = s.Query.GetSelectedBeings.Ask();
            var species = source.Species;
            var isAlive = source.IsAlive;

            foreach (var being in s.Beings.All) {
                if (s.Query.IsSelectionFull.Ask()) { break; }
                if (being.Species == species && being.IsAlive == isAlive && RenderUtils.IsVisible(being.Position)) {
                    if (!selectedBeings.Contains(being)) {
                        being.Transmit(null, MsgIdH.EntitySelected);
                        selectedBeings.Add(being);
                    }
                }
            }

            s.Sig.BeingsSelected.Send(selectedBeings);
        }

        private static void SelectSimilarVisibleObjs(GameState s, Obj source) {
            if (!source.IsActive) {
                return;
            }

            s.Query.DeselectAny.Ask();
            var selectedObjs = s.Query.GetSelectedObjs.Ask();
            var def = source.Definition;

            foreach (var obj in s.Objs.All) {
                if (s.Query.IsSelectionFull.Ask()) { break; }
                if (obj.Definition == def && RenderUtils.IsVisible(obj.Position)) {
                    if (!selectedObjs.Contains(obj)) {
                        obj.Transmit(null, MsgIdH.EntitySelected);
                        selectedObjs.Add(obj);
                    }
                }
            }

            s.Sig.ObjSelected.Send(selectedObjs);
        }

        private static void SelectSimilarVisibleTiles(GameState s, Tile source) {
            if (!source.IsActive) {
                return;
            }

            s.Query.DeselectAny.Ask();
            var selectedTiles = s.Query.GetSelectedTiles.Ask();
            var def = source.Definition;
            var isConstructed = source.IsConstructed;

            foreach (var entity in s.Grids[def.LayerId].Data) {
                if (s.Query.IsSelectionFull.Ask()) { break; }
                if (entity?.IsActive == true && entity.Definition == def
                        && entity.IsConstructed == isConstructed
                        && RenderUtils.IsVisible(entity.Position)) {
                    if (!selectedTiles.Contains(entity)) {
                        if (entity.Transform.IsChild) {
                            entity.Transform.Parent.Transmit(null, MsgIdH.EntitySelected);
                        } else {
                            entity.Transmit(null, MsgIdH.EntitySelected);
                        }
                        selectedTiles.Add(entity);
                    }
                }
            }

            s.Sig.TilesSelected.Send(selectedTiles);
        }
    }
}
