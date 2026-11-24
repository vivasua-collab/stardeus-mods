using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class JobMatrixWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText panelHeader;
        [SerializeField] private WGroup topContainer;
        [SerializeField] private WGroup content;
        [SerializeField] private WGroup footer;
        [SerializeField] private GameObject footerBlock;
        [SerializeField] private ScrollRect scroll;
        public WGroup TopContainer => topContainer;
        public WGroup Content => content;

        public Action OnHide;

        private void Awake() {
            topContainer.DestroyChildren();
            footer.DestroyChildren();
            content.DestroyChildren();
            panelHeader.Load(T.NounColonyJobs, Close);
            footerBlock.SetActiveFast(false);
            gameObject.SetActiveFast(false);
        }

        public void Initialize() {
            Register(UIWidgetId.JobMatrix, this);
        }

        public override void Toggle(bool show) {
            base.Toggle(show);
            UIShowing.Set(UIShowingFlags.JobMatrix, show);
            if (!show) {
                OnHide?.Invoke();
            } else {
                scroll.verticalNormalizedPosition = 1f;
                Rect.SetAsLastSibling();
            }
        }

        public void Close() {
            Toggle(false);
        }

        public bool TryCloseWidget() {
            Close();
            return true;
        }
    }
}
