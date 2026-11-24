using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class TutorialPopupWidget : BaseWidget {
        [SerializeField] private WText comingUpTxt;
        [SerializeField] private WText tutorialNameTxt;
        [SerializeField] private WProgress progress;
        [SerializeField] private WButtonText skipButton;
        [SerializeField] private WButtonText openButton;
        [SerializeField] private Image scrollingImage;
        [SerializeField] private RectTransform background;
        private Material mat;
        private float timeoutLeft;
        private const float waitTimeSeconds = 5f;

        public void Close() {
            UIShowing.Unset(UIShowingFlags.TutorialPopup);
            Toggle(false);
        }

        private void Update() {
            // Pause the auto-open so the full tutorial never covers a dialog or a bug report
            if (UIShowing.MainMenu || UIShowing.Feedback
                    || (The.Dialog != null && The.Dialog.IsBusy)) { return; }
            if (timeoutLeft > 0f) {
                var dt = Time.unscaledDeltaTime;
                timeoutLeft -= dt;
                // D.Warn("TL, dt: {0}/{1}", timeoutLeft, dt);
                if (timeoutLeft < 0) {
                    openButton.Click();
                } else {
                    progress.SetValue(1f - timeoutLeft / waitTimeSeconds);
                }
            }
        }

        public void ShowForCodexPage(CodexConceptPage page) {
            UISounds.Play("FX/Hail_Yes_SFX", vol: 0.5f);
            comingUpTxt.Load(Texts.Grey(T.OpeningTutorial));
            tutorialNameTxt.Load(page.TitleT);
            if (mat == null) {
                mat = new Material(scrollingImage.material);
                mat.SetColor("_StartColor",
                    Colors.WithA(The.UITheme.GetColor("Accent", this), 0.05f));
                mat.SetColor("_EndColor",
                    Colors.WithA(The.UITheme.GetColor("Accent", this), 0.35f));
                scrollingImage.material = mat;
            }
            openButton.Load(T.Show, () => {
                Close();
                A.S.UIQuery.ShowTutorial.Ask(page.Id);
            });
            skipButton.Load(T.Skip, () => {
                Close();
            });
            progress.Load(The.UITheme.GetColor("Accent", this), 1f);
            timeoutLeft = waitTimeSeconds;
            UIShowing.Set(UIShowingFlags.TutorialPopup);
            Toggle(true);
            // The toast auto-opens a full-screen tutorial, so point the player at it first.
            // Ring the Background: it sticks out past the widget root and would hide that ring.
            UIFocusRing.Request(background);
        }
    }
}
