using System;
using Game.Systems.Creatures;
using KL.Pool;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WBeingGroup : WGroup {
        [SerializeField] private GridLayoutGroup grid;
        public override string ToString() {
            return "WBeingGroup[]";
        }

        public void SetColumns(int cols) {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cols;
        }
    }
}
