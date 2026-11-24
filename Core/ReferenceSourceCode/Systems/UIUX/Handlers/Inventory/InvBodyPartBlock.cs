using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class InvBodyPartBlock {
        public static readonly IComparer<InvBodyPartBlock> Comparer = Comparer<InvBodyPartBlock>.Create((a, b) =>
            string.Compare(a.Type.Def.NameT, b.Type.Def.NameT, StringComparison.Ordinal));

        private readonly WButtonIconText2 prefab;
        private WButtonIconText2 widget;
        public readonly BodyPartDef Type;
        public readonly List<ObjData> Objs = new();
        private int lastCount = -1;
        public int Count => lastCount;
        private string lastCountTxt = null;
        private bool isShowing;
        public int Side;

        public InvBodyPartBlock(BodyPartDef def) {
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
            widget.Load(Type.Def.Preview, Type.Def.NameT, lastCountTxt, ClickBodyPart);
            UIUtils.AddCodexTooltip(life, widget.gameObject, popupRoot, Type.Def,
                showDelay: Tooltip.DelaySeconds, optional: false);
            UIUtils.AddCodexTooltip(life, widget.Text2.gameObject, popupRoot,
                Type.Def, showDelay: Tooltip.DelaySeconds, isChartMode: true);
        }

        private int findIdx;

        private void ClickBodyPart() {
            findIdx = BodyPartSearchResult.FocusNext(Type, findIdx);
        }

        public void SetCount(int count) {
            if (count == lastCount) { return; }
            // D.Warn("Updating body part equipment count: {0} -> {1} -> {2}",
            //     Type.Id, lastCount, count);
            lastCount = count;
            var text = Units.AbbrevNum(count);
            if (lastCountTxt == text) { return; }
            lastCountTxt = text;
            if (!isShowing) { return; }
            widget.SetText2(text);
        }
    }
}
