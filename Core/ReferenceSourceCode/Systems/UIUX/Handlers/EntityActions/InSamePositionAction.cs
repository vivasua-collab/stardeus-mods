using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class InSamePositionAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new InSamePositionAction());
        }

        public const string ActionId = "InSamePosition";
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Being being, out EntityActionData data) {
            if (!being.IsActive) {
                data = EntityActionData.Unavailable;
                return false;
            }
            var others = CollectAtBeingPos(s, being);
            if (others.Count == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, others),
                Priority.Low);
            return true;
        }

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!obj.IsActive) {
                data = EntityActionData.Unavailable;
                return false;
            }
            var others = CollectAtObjPos(s, obj);
            if (others.Count == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, others),
                Priority.Low);
            return true;
        }

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!tile.IsActive) {
                data = EntityActionData.Unavailable;
                return false;
            }
            var others = CollectAtTilePos(s, tile);
            if (others.Count == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, others),
                Priority.Low);
            return true;
        }

        private static List<Entity> CollectAtObjPos(GameState s, Obj obj) {
            var result = new List<Entity>();
            int pos = obj.PosIdx;
            AddTilesAtPos(s, pos, result, obj);
            AddObjsAtPos(s, pos, result, obj);
            AddBeingsAtPos(s, pos, result, obj);
            return result;
        }

        private static List<Entity> CollectAtTilePos(GameState s, Tile tile) {
            var result = new List<Entity>();
            var posCount = tile.Transform.GetSubtilePos(Caches.IntArray128);
            for (int pi = 0; pi < posCount; pi++) {
                int pos = Caches.IntArray128[pi];
                AddTilesAtPos(s, pos, result, tile);
                AddObjsAtPos(s, pos, result, tile);
                AddBeingsAtPos(s, pos, result, tile);
            }
            return result;
        }

        private static List<Entity> CollectAtBeingPos(GameState s, Being being) {
            var result = new List<Entity>();
            int pos = being.PosIdx;
            AddTilesAtPos(s, pos, result, being);
            AddObjsAtPos(s, pos, result, being);
            AddBeingsAtPos(s, pos, result, being);
            return result;
        }

        private static void AddTilesAtPos(GameState s, int pos, List<Entity> result, Entity exclude) {
            foreach (var li in RenderLayers.DataLayerIds) {
                var t = s.Grids[li].Get(pos);
                if (t == null || !t.IsActive) { continue; }
                if (t.Transform.IsChild) { t = t.Transform.Parent; }
                if (t == exclude) { continue; }
                if (!IsTileSelectable(t)) { continue; }
                if (!result.Contains(t)) {
                    result.Add(t);
                }
            }
        }

        private static void AddObjsAtPos(GameState s, int pos, List<Entity> result, Entity exclude) {
            if (!s.Objs.ByPos.TryGetValue(pos, out var objs)) { return; }
            foreach (var obj in objs) {
                if (!obj.IsActive) { continue; }
                if (obj == exclude) { continue; }
                result.Add(obj);
            }
        }

        private static void AddBeingsAtPos(GameState s, int pos, List<Entity> result, Entity exclude) {
            if (!s.Beings.ByPos.TryGetValue(pos, out var beings)) { return; }
            foreach (var being in beings) {
                if (!being.IsActive) { continue; }
                if (being == exclude) { continue; }
                if (!result.Contains(being)) {
                    result.Add(being);
                }
            }
        }

        private static bool IsTileSelectable(Tile t) {
            return t.ENode != null
                || t.Definition.LayerId == WorldLayer.Objects
                || t.HasComponent<EmblemComp>()
                || !t.IsConstructed;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, GameState s, List<Entity> others) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CSelectSimilar, T.SelectOthersHere,
                () => ShowChoice(s, others));
            UIUtils.AddTip(l, widget, T.SelectOthersHereTip);
            return widget;
        }

        private static void ShowChoice(GameState s, List<Entity> others) {
            var qc = QuickChoice.Create();
            int total = others.Count;
            int shown = Math.Min(10, total);
            for (int i = 0; i < shown; i++) {
                var entity = others[i];
                qc.AddButton(EntityLabel(entity), () => s.Sig.SelectEntity.Send(entity));
            }
            if (total > 10) {
                qc.AddButton(Texts.Grey($"+{total - 10}"), () => { }, false);
            }
            qc.Show();
        }

        private static string EntityLabel(Entity entity) {
            // Same markers as the multi-select panel, to tell the things on one tile apart
            if (entity is Obj obj) {
                return ItemOwnership.DecoratedItemName(obj.Data, null, obj.Data.ListName);
            }
            if (entity is Tile tile) { return tile.Definition.CodexLink; }
            if (entity is Being being) { return BeingUtils.DecoratedBeingName(being.Data, false); }
            return entity.ToString();
        }
    }
}
