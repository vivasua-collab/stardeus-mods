using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WBattleStationMiniIcon : WButtonIcon, IPointerEnterHandler, IPointerExitHandler, ICustomPoolable {
        private BattleStationComp comp;

        public bool IsUnmanned;

        public void Load(BattleStationComp comp) {
            this.comp = comp;
            SetOnClick(() => {
                EntityUtils.SelectOrFocus(comp.Entity);
            });
            UpdateState();
        }
        public void OnPointerEnter(PointerEventData eventData) {
            if (comp == null) { return; }
            EntityUtils.ShowEntityLink(null, comp.Tile, true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (comp == null) { return; }
            EntityUtils.HideEntityLink();
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public void PoolDeactivate() {
            IsUnmanned = false;
            comp = null;
            SetTint(Consts.ColorWhite);
            gameObject.SetActiveFast(false);
        }

        public void UpdateState() {
            if (comp == null) { return; }
            var hasWeapons = comp.AssignedWeapons.Value > 0;
            var hasOperator = comp.IsOperatorPresent;
            IsUnmanned = !hasOperator;
            if (hasWeapons && hasOperator) {
                SetTint(Consts.ColorChartGreen);
            } else if (hasWeapons) {
                SetTint(Consts.ColorChartYellow);
            } else {
                SetTint(Color.gray);
            }
        }
    }
}
