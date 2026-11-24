#if !PRODUCTION_BUILD
// #define DEBUG_BAR
#endif
using System;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UI.InGame {
    public class InGameProgressBar : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler, ICustomPoolable {
        [SerializeField] private Transform fillTr;
        [SerializeField] private SpriteRenderer fillSprite;
        [SerializeField] private SpriteRenderer backSprite;
        [SerializeField] private SpriteRenderer frontSprite;
        [SerializeField] private Transform fillTr2;  // Optional second fill transform
        [SerializeField] private SpriteRenderer fillSprite2;  // Optional second fill sprite
        [SerializeField] private BoxCollider boxCollider;
        private const float barOffsetLeft = 0.09f;
        private const float barOffsetRight = 0.18f;

        public Transform Transform;
        private Func<string> tooltip;
        private float progress;
        private Vector2 initialFillSpriteSize;
        private Vector2 initialBackSpriteSize;
        private Vector2 initialFrontSpriteSize;
        private Vector2 currentFillSpriteSize; // New field to remember the current fill sprite size
        private Vector2 initialFillSprite2Size;  // Initial size for second fill sprite
        private Vector2 currentFillSprite2Size;  // Current size for second fill sprite
        private bool isInitialized;

        private void Awake() {
            Transform = transform;
            initialFillSpriteSize = fillSprite.size;
            initialBackSpriteSize = backSprite.size;
            initialFrontSpriteSize = frontSprite.size;
            currentFillSpriteSize = initialFillSpriteSize; // Initialize current size

            if (fillSprite2 != null) {
                initialFillSprite2Size = fillSprite2.size;
                currentFillSprite2Size = initialFillSprite2Size;
            }
            isInitialized = true;
        }
        public void SetFillColor(Color color) {
            fillSprite.color = color;
        }

        public void SetFillColor2(Color color) {
            if (fillSprite2 != null) {
                fillSprite2.color = color;
            }
        }

        public void SetWidth(float width) {
            if (!isInitialized) {
                D.Err("Trying to set width for an uninitialized progress bar! {0} | {1}",
                    this, width);
                return;
            }
            var size = initialFillSpriteSize;
            size.x = width;
            fillSprite.size = size;
            currentFillSpriteSize = size; // Remember the new size
            boxCollider.size = size;

            if (fillSprite2 != null) {
                size = initialFillSprite2Size;
                size.x = width;
                fillSprite2.size = size;
                currentFillSprite2Size = size;
            }

            SetFillX();

            size = initialBackSpriteSize;
            size.x = width;
            backSprite.size = size;
            size = initialFrontSpriteSize;
            size.x = width;
            frontSprite.size = size;
        }

        private void SetFillX() {
            Vector3 fillPos = fillTr.localPosition;
            fillPos.x = fillSprite.size.x * -0.5f + barOffsetLeft;
            fillTr.localPosition = fillPos;

            if (fillSprite2 != null && fillTr2 != null) {
                Vector3 fillPos2 = fillTr2.localPosition;
                fillPos2.x = fillSprite2.size.x * -0.5f + barOffsetLeft;
                fillTr2.localPosition = fillPos2;
            }
        }
        public void ResetWidth() {
            if (!isInitialized) {
                D.Err("Trying to reset width for an uninitialized progress bar! {0}",
                    this);
                return;
            }
            fillSprite.size = initialFillSpriteSize;
            currentFillSpriteSize = initialFillSpriteSize; // Reset the current size

            if (fillSprite2 != null) {
                fillSprite2.size = initialFillSprite2Size;
                currentFillSprite2Size = initialFillSprite2Size;
            }

            backSprite.size = initialBackSpriteSize;
            frontSprite.size = initialFrontSpriteSize;
            SetFillX();
        }
        public void SetBackColor(Color color) {
            backSprite.color = color;
        }
        public void SetTooltip(Func<string> tip) {
            boxCollider.enabled = tip != null;
            tooltip = tip;
        }

        public void SetProgress(float progress) {
            this.progress = Mathf.Clamp01(progress);
            // fillTr.localScale = new Vector3(this.progress, 1f, 1f);
            if (!isInitialized) {
                D.Err("Trying to set progress for an uninitialized progress bar! {0} | {1}",
                    this, progress);
                return;
            }

            // Set the fill sprite size with x multiplied by progress
            Vector2 newSize = currentFillSpriteSize;
            var newX = currentFillSpriteSize.x * this.progress;
            if (newX > barOffsetRight)  {
                newX -= barOffsetRight;
            }

            newSize.x = newX;
            fillSprite.size = newSize;
        }

        public void SetProgress2(float progress) {
            if (fillSprite2 != null) {
                if (!isInitialized) {
                    D.Err("Trying to set progress for an uninitialized progress bar! {0} | {1}",
                        this, progress);
                    return;
                }
                float clampedProgress = Mathf.Clamp01(progress);
                Vector2 newSize = currentFillSprite2Size;
                var newX = currentFillSprite2Size.x * clampedProgress;
                if (newX > barOffsetRight)  {
                    newX -= barOffsetRight;
                }
                newSize.x = newX;
                fillSprite2.size = newSize;
            } else {
                D.Err("Trying to set progress2 for a progress bar without a second fill sprite! {0}",
                    this);
            }
        }

        #if DEBUG_BAR
        private void Update() {
            var prog = progress + Time.unscaledDeltaTime;
            if (prog >= 1f) {
                prog = 0f;
            }
            SetProgress(prog);
        }
        #endif

        public void OnPointerEnter(PointerEventData eventData) {
            if (tooltip != null) {
                The.SysSig.ShowDynamicTooltip.Send(tooltip, true);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            The.SysSig.HideTooltip.Send();
        }

        public void PoolDeactivate() {
            ResetWidth();
            SetTooltip(null);
            // PrefabPool restores position and rotation on respawn, but not scale, and
            // this pool is shared: ShipZoneView scales its bars to cancel out the zone
            // transform, while ShipView expects the prefab default. Without this, a bar
            // handed from one to the other renders at the previous owner's scale.
            transform.localScale = Vector3.one;
            gameObject.SetActive(false);
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }
    }
}
