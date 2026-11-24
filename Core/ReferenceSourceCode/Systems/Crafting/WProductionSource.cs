using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Crafting {
    public class WProductionSource : WBase {
        [SerializeField] private  WIconText2 item;
        [SerializeField] private GameObject arrowBase;

        public void Load(Lifetime life, GameState s, Mat mat, bool showArrowBase) {
            arrowBase.SetActiveFast(showArrowBase);
            item.Load(mat.Type.IconId, mat.Type.NameT, null);

            var matB = s.UIQuery.GetBindableMatCountFor.Ask(mat.Type);
            matB.Bind(life, (cnt) => {
                item.SetText2($"{mat.MaxStackSize}/{Units.NumColoredAbbr(cnt)}");
            });
            UIUtils.AddCodexTooltip(life, item.Text.gameObject, this, mat.Type.Def,
                optional: false, showDelay: Tooltip.DelaySeconds);
            UIUtils.AddCodexTooltip(life, item.Text2.gameObject, this, mat.Type.Def,
                optional: false, showDelay: Tooltip.DelaySeconds, isChartMode: true);
        }
    }
}
