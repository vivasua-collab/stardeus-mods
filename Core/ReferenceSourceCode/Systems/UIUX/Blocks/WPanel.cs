using System;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class WPanel : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText header;
        public ScrollRect Scroll;
        public WGroup Content;
        public WGroup Footer;

        public void Initialize() {
            Content.DestroyChildren();
            ShowFooter(false);
            Footer?.DestroyChildren();
        }

        public void Cleanup() {
            ShowFooter(false);
        }

        public void ShowFooter(bool yes) {
            Footer?.SetActiveFast(yes);
        }

        public override void Toggle(bool show) {
            if (show) {
                Scroll.verticalNormalizedPosition = 1f;
            }
            base.Toggle(show);
        }

        public void Load(Action onClick) {
            Load(null, onClick);
        }
        public void Load(string title, Action onClick) {
            header.Load(title, onClick);
        }

        public bool TryCloseWidget() {
            header.Click();
            // Toggle(false);
            return true;
        }
    }
}
