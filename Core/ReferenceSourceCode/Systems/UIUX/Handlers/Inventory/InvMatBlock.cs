using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class InvMatBlock {
        public static readonly IComparer<InvMatBlock> Comparer = Comparer<InvMatBlock>.Create((a, b) =>
            string.Compare(a.Type.NameT, b.Type.NameT, StringComparison.Ordinal));

        private readonly WInventoryMatItem prefab;
        private WInventoryMatItem widget;
        private readonly GameState S;
        public readonly MatType Type;
        private int lastCount = -1;
        public int Count => lastCount;
        private string lastCountTxt = null;
        private int favThreshold;
        private bool isShowing => widget?.IsDisposed == false;

        public InvMatBlock(GameState s, MatType mat) {
            S = s;
            prefab = ResourceLibrary.FindPrefab<WInventoryMatItem>(UIPrefabId.WInventoryMatItem);
            Type = mat;
        }

        public void UpdateFavState(bool fav, int threshold) {
            favThreshold = fav ? threshold : 0;
            if (!isShowing) { return; }
            widget.Toggle.Toggle.SetIsOnWithoutNotify(fav);
            widget.Toggle.SetText(NameWithThreshold());
        }

        // A thresholded fav stays checked here but is hidden from the quick drawer while
        // well stocked, so the threshold has to be visible or the checkbox contradicts it.
        private string NameWithThreshold() {
            if (favThreshold <= 0) { return Type.Def.NameT; }
            return $"{Type.Def.NameT} {Texts.GreySmaller($"< {Units.Num(favThreshold)}")}";
        }

        public void SpawnWidget(Lifetime life, RectTransform container, WBase popupRoot, int idx) {
            // if (widget != null && !widget.IsDisposed) {
            //     D.Err("InvMatBlock trying to spawn widget when one already exists: {0}", Type.Id);
            // } else {
            // }
            widget = UISpawn.Widget(life, prefab, container, idx);
            UIUtils.AddCodexTooltip(life, widget.CodexTipTarget, popupRoot, Type.Def,
                showDelay: Tooltip.DelaySeconds, optional: false);
            UIUtils.AddCodexTooltip(life, widget.CodexChartTarget, popupRoot,
                Type.Def, showDelay: Tooltip.DelaySeconds, isChartMode: true);
            var fav = S.Query.IsFavMat.Ask(Type);
            favThreshold = fav.isFav ? fav.countThreshold : 0;
            var toggle = widget.Toggle;
            toggle.SetImage(Type.IconId);
            toggle.SetTint(Type.IconTint);
            toggle.SetText(NameWithThreshold());
            toggle.SetText2(lastCountTxt);
            toggle.LoadToggle(fav.isFav);
            toggle.SetOnToggle(ToggleFavMat);
            widget.Button.Load(ClickMat);
        }


        private void ClickMat() {
            FavMatBlock.ShowToggleFavQuickChoice(Type, widget.Rect);
        }

        private void ToggleFavMat(bool yes) {
            S.Sig.SetFavMat.Send(Type, yes, 0);
        }


        public void SetCount(int count) {
            if (count == lastCount) { return; }
            // D.Warn("Updating inv mat count: {0} -> {1} -> {2}",
            //     Type.Id, lastCount, count);
            lastCount = count;
            var text = Units.NumColoredAbbr(count);
            if (lastCountTxt == text) { return; }
            lastCountTxt = text;
            if (!isShowing) { return; }
            widget.Toggle.SetText2(text);
        }
    }
}
