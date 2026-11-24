using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class UIFadeOnEnable : MonoBehaviour {
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private float fadeInTimeSeconds = 0.25f;
        private void Awake() {
            cg.alpha = 0f;
        }

        private void Update() {
            if (cg.alpha < 1f) {
                cg.alpha += 1f / fadeInTimeSeconds * GameTimer.unscaledDeltaTime;
            }
        }

        private void OnDisable() {
            cg.alpha = 0f;
        }
    }
}
