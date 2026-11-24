using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class TutorialWidget : BaseWidget {
        // page title goes here
        [SerializeField] private WText header;
        // content goes here
        [SerializeField] private WGroup container;
        // closes the tutorial widget
        [SerializeField] private WButton closeButton;
        [SerializeField] private WButtonText markAsLearnedButton;
        // wire to show open the codex of the current page + close widget
        [SerializeField] private WButtonText openCodexButton;
        // TODO - scroll up whenever loading a new page
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] public TutorialPopupWidget Popup;
        private GameState S;
        private Lifetime lifetime;
        public CodexConceptPage CurrentPage { get; private set; }

        private void Awake() {
            closeButton.Load(Close);
            Close();
        }

        public void Initialize(GameState s) {
            container.DestroyChildren();
            Register(UIWidgetId.Tutorial, this);
            Popup.Close();
            S = s;
        }

        public void Close() {
            Toggle(false);
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Tutorial, show);
            base.Toggle(show);
            if (show) {
                S.Clock.ForceStop(KL.Clock.ForceStopReason.Cutscene);
                S.Toolbox.Clear();
                Rect.SetAsLastSibling();
            } else {
                CleanupLife();
                S?.Clock?.ForceResume(KL.Clock.ForceStopReason.Cutscene);
            }
        }

        public void ShowCodexPage(CodexConceptPage page) {
            CleanupLife();
            lifetime = Lifetime.Create("Tutorial");
            CurrentPage = page;
            header.Load(page.TitleT);
            page.Build(S, lifetime, container, 0);
            openCodexButton.Load(T.OpenCodex, () => {
                Close();
                S.UIQuery.ShowCodexEntry.Ask(page);
            });
            markAsLearnedButton.Load(T.CodexMarkLearned, () => {
                page.IsLearned.Set(true);
                Close();
            });
            scrollRect.verticalNormalizedPosition = 1f;
            Toggle(true);
            UISounds.PlayAchievement();
        }

        private void CleanupLife() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }
    }
}
