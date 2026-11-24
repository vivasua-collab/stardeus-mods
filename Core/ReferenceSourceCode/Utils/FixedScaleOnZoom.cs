using Game.Rendering;
using UnityEngine;

namespace Game.Utils {
    public sealed class FixedScaleOnZoom : MonoBehaviour {
        /// <summary>
        /// scale needs to be extremely small, so in order to not lose
        /// floating point precision, we'll use double adjustment,
        /// and keep the scale much larger that way
        /// </summary>
        private const double scaleAdjustment = 0.0001d;
        /// <summary>
        /// Sprite width will be set here, so it will be in hundreds or even thousands
        /// </summary>
        [HideInInspector] public float Size;
        public float Scale = 1f;
        private float lastOrtho = -1;

        private void Start() {
            if (Size == 0) {
                var sr = GetComponent<SpriteRenderer>();
                Size = sr?.sprite?.rect.width ?? 128;
            }
        }

        private void Update() {
            var ortho = RenderUtils.CameraOrtho;
            if (lastOrtho != ortho) {
                lastOrtho = ortho;
                // result won't be too small of a number
                double s = scaleAdjustment * Scale * Size * ortho;
                float sf = (float) s;
                transform.localScale = new Vector3(sf, sf, sf);
            }
        }

        [ContextMenu("Reset Scale")]
        public void ResetScale() {
            lastOrtho = -1;
        }
    }
}
