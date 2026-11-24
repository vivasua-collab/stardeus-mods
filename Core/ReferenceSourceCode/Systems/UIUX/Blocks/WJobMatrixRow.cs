using System;
using System.Collections.Generic;
using Game.Constants;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WJobMatrixRow : WBase {
        [SerializeField] private WButtonIconText wBeingBtn;
        [SerializeField] private WGroup container;

        public WButtonIconText BeingBtn => wBeingBtn;
        public WGroup ToggleContainer => container;

        private List<WJobMatrixPriority> cells;

        public bool HasCells => cells != null;

        public void Load(Sprite avatar, string name, Action onClick) {
            wBeingBtn.Load(avatar, name);
            wBeingBtn.SetOnClick(onClick);
        }

        public void Load(string iconId, string name, Action onClick) {
            wBeingBtn.Load(iconId, name);
            wBeingBtn.SetOnClick(onClick);
        }

        public void EnsureCells(int count) {
            if (cells != null) { return; }
            cells = new List<WJobMatrixPriority>(count);
            for (int i = 0; i < count; i++) {
                var c = UISpawn.WidgetPermanent<WJobMatrixPriority>(
                    UIPrefabId.WJobMatrixTogglePrio, container.Container);
                cells.Add(c);
            }
        }

        public WJobMatrixPriority GetCell(int index) => cells[index];
    }
}
