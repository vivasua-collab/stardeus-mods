using System;
using Game.UI;
using KL.Pool;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WTabRow : WGroup {
        [SerializeField] private CanvasGroup cg;
        public void Hide() {
            UIUtils.ToggleCG(cg, false);
        }
        public void Show() {
            UIUtils.ToggleCG(cg, true);
        }
        public override string ToString() {
            return "WTabRow[]";
        }
    }
}
