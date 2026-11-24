using System;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class WGameApp : BaseWidget, ICloseableWidget, ICustomPoolable {
        [SerializeField] private WButtonText header;
        public WRawImage RawImage;
        public WGroup Footer;
        public void Cleanup() {
            ShowFooter(false);
        }

        public void ShowFooter(bool yes) {
            Footer?.SetActiveFast(yes);
        }

        public void Load(string title, Action onClick) {
            header.Load(title, onClick);
        }

        public bool TryCloseWidget() {
            header.Click();
            // Toggle(false);
            return true;
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
            A.S.Clock.ExecuteAfterFramesForUI(1, () => {
                UIUtils.AfterShowPopup(Rect, centerOfScreen: true);
            });
        }
    }
}
