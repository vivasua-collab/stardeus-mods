using Game.Constants;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI {
    public sealed class ErrorMessage : MonoBehaviour {
        [SerializeField] private TMP_Text message;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private RectTransform rekt;
        [SerializeField] private CanvasGroup cg;

        private float timeLeft;

        private void Hide() {
            cg.alpha = 0;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        private Material backgroundMat;

        private void Awake() {
            // A copy, as UIDialogBox does: Image.material is the shared material, so the
            // flash time went into every user of it and dirtied the asset in the Editor
            backgroundMat = new Material(backgroundImage.material);
            backgroundMat.hideFlags = HideFlags.DontSave;
            backgroundImage.material = backgroundMat;
            Hide();
        }

        private void OnDestroy() {
            if (backgroundMat != null) {
                Destroy(backgroundMat);
                backgroundMat = null;
            }
        }

        public void ShowMessage(string txt) {
            message.SetText(txt);
            cg.interactable = true;
            cg.blocksRaycasts = true;
            backgroundMat.SetFloat(ShaderVars._FlashStartTime, GameTimer.unscaledTime);
            cg.alpha = 1f;
            timeLeft = 3f;
        }

        public void OnUpdate() {
            if (timeLeft <= 0f) {
                return;
            }
            timeLeft -= GameTimer.unscaledDeltaTime;
            if (timeLeft <= 0f) {
                Hide();
                message.SetText(string.Empty);
            }
        }
    }
}
