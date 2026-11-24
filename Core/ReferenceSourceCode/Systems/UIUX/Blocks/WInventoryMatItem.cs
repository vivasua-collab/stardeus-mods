using System;
using KL.UI;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WInventoryMatItem : WBase {
        public WToggleIconText2 Toggle;
        public WButton Button;
        public GameObject CodexTipTarget => Toggle.Text.gameObject;
        public GameObject CodexChartTarget => Toggle.Text2.gameObject;
    }
}
