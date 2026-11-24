using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class InvEquipmentBlock {
        public static readonly IComparer<InvEquipmentBlock> Comparer = Comparer<InvEquipmentBlock>.Create((a, b) =>
            string.Compare(a.Type.Def.NameT, b.Type.Def.NameT, StringComparison.Ordinal));

        private readonly WButtonIconText2 prefab;
        private WButtonIconText2 widget;
        public readonly InventoryItemDef Type;
        public readonly List<ObjData> Objs = new();
        private int lastCount = -1;
        private int lastEquippedCount = -1;
        public int Count => lastCount;
        private string lastCountTxt = null;
        private bool isShowing;
        public int Side;

        public InvEquipmentBlock(InventoryItemDef def) {
            prefab = ResourceLibrary.FindPrefab<WButtonIconText2>(UIPrefabId.WInventoryItem);
            Type = def;
        }

        public void Cleanup() {
            if (widget != null) {
                PrefabPool.Despawn(widget.gameObject);
                widget = null;
            }
            isShowing = false;
        }

        public void SpawnWidget(Lifetime life, RectTransform container, WBase popupRoot, int idx) {
            isShowing = true;
            widget = UISpawn.Widget(life, prefab, container, idx);
            widget.Load(Type.Def.Preview, Type.Def.NameT, lastCountTxt, ClickEquipment);
            UIUtils.AddCodexTooltip(life, widget.gameObject, popupRoot, Type.Def,
                showDelay: Tooltip.DelaySeconds, optional: false);
            UIUtils.AddTipFunc(life, widget, () => {
                if (lastEquippedCount == 0) { return null; }
                var availableCount = lastCount - lastEquippedCount;
                return $"{T.ItemStateEquipped}: {lastEquippedCount}/{availableCount + lastEquippedCount}";
            });
            UIUtils.AddCodexTooltip(life, widget.Text2.gameObject, popupRoot,
                Type.Def, showDelay: Tooltip.DelaySeconds, isChartMode: true);
        }

        private int findIdx;
        private readonly Dictionary<Color, int> colorFindIdx = new();

        private void ClickEquipment() {
            if (Type.ColorGroupId != null) {
                var palette = Palette.ForGroup(Type.ColorGroupId);
                if (palette == null || palette.Count == 0) {
                    findIdx = EquipmentSearchResult.FocusNext(Type, findIdx);
                    return;
                }

                var foundObjs = Caches.ObjDataList;
                foundObjs.Clear();
                A.S.Query.FindObjs.Ask(foundObjs, o =>
                    o.Item == Type &&
                    o.State != ObjState.Destroyed &&
                    o.State != ObjState.InDevice);

                if (foundObjs.Count == 0) { return; }

                var qc = QuickChoice.Create(Type.ItemName);
                qc.Widget = widget.Rect;

                foreach (var colorDef in palette) {
                    int count = 0;
                    foreach (var obj in foundObjs) {
                        if (obj.Color == colorDef.Color) { count++; }
                    }
                    if (count == 0) { continue; }

                    var capturedColor = colorDef.Color;
                    qc.AddIconButton($"{colorDef.NameT} ({Units.AbbrevNum(count)})", new QuickChoice.IconButton() {
                        Icon = Type.Def.PreviewSprite,
                        Tint = colorDef.Color,
                        AutoClose = false,
                        OnClick = () => {
                            if (!colorFindIdx.TryGetValue(capturedColor, out var idx)) { idx = 0; }
                            var objs = new List<ObjData>();
                            A.S.Query.FindObjs.Ask(objs, o =>
                                o.Item == Type &&
                                o.State != ObjState.Destroyed &&
                                o.State != ObjState.InDevice &&
                                o.Color == capturedColor);
                            if (objs.Count == 0) { return; }
                            idx %= objs.Count;
                            EquipmentSearchResult.FocusOnObj(objs[idx]);
                            colorFindIdx[capturedColor] = (idx + 1) % objs.Count;
                        },
                    });
                }

                if (qc.Count == 0) { return; }
                qc.AddButton(T.FindAnyColor, () => {
                    findIdx = EquipmentSearchResult.FocusNext(Type, findIdx);
                }, autoClose: false);
                qc.Show();
            } else {
                findIdx = EquipmentSearchResult.FocusNext(Type, findIdx);
            }
        }

        public void SetCount(int count, int equippedCount = 0) {
            if (count == lastCount && equippedCount == lastEquippedCount) { return; }
            lastCount = count;
            lastEquippedCount = equippedCount;
            string text;
            if (equippedCount > 0) {
                text = Texts.Grey($"{Units.AbbrevNum(equippedCount)}/");
                text = $"{text}{Units.AbbrevNum(count)}";
            } else {
                text = Units.AbbrevNum(count);
            }
            if (lastCountTxt == text) { return; }
            lastCountTxt = text;
            if (!isShowing) { return; }
            widget.SetText2(text);
        }
    }
}
