using System;
using Game.Constants;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Game.Systems.UIUX {
    // Inline, autoplaying/looping/muted video block for codex pages (see
    // CodexConceptPage.AddVideoBlock). Mirrors WButtonImageAutoSize, but renders a
    // VideoClip into a RenderTexture/RawImage instead of an Image/Sprite. Clicking
    // it fires the supplied action (CodexConceptPage wires this to
    // SysSig.ShowVideoBlowout).
    [DisallowMultipleComponent]
    public class WVideoBlockAutoSize : WBase, ICustomPoolable {
        [SerializeField] private Button button;
        [SerializeField] private RawImage rawImage;
        [SerializeField] private LayoutElement layoutElement;
        [SerializeField] private VideoPlayer player;
        [SerializeField] private RectTransform parentRect;
        [SerializeField] private RectOffset parentPadding;
        private Action onClick;
        private RenderTexture rt;
        private int maxHeight;
        private float lastColWidth = -1f;

        private void Awake() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
            player.isLooping = true;
            player.playOnAwake = true;
            player.waitForFirstFrame = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
        }

        public void Load(VideoClip clip, Action act, int heightLimit = 512) {
            onClick = act;
            maxHeight = heightLimit;
            SetClip(clip);
            Coroutines.OnNextFrame(Calculate);
        }

        private void SetClip(VideoClip clip) {
            ReleaseRT();
            if (clip == null) {
                return;
            }
            rt = RenderTexture.GetTemporary((int)clip.width, (int)clip.height);
            rawImage.texture = rt;
            player.targetTexture = rt;
            player.clip = clip;
            player.Play();
        }

        // RawImage has no preserveAspect (unlike Image), so it stretches to fill whatever
        // rect it's given. To avoid distortion we size BOTH dimensions to the clip's aspect
        // ratio here (never upscaling past native size) and let the container center the
        // resulting rect (HorizontalLayoutGroup.ChildForceExpandWidth=0 + ChildAlignment
        // =MiddleCenter on WTabPanelItem_VideoBlock) -- this is the letterbox.
        private void Calculate() {
            if (player.clip == null) {
                layoutElement.preferredWidth = 0;
                layoutElement.preferredHeight = 0;
                return;
            }
            var colWidth = parentRect.rect.width - parentPadding.left - parentPadding.right;
            lastColWidth = colWidth;
            var clipW = (float)player.clip.width;
            var clipH = (float)player.clip.height;
            var aspect = clipH / clipW;
            var width = Mathf.Min(colWidth, clipW);
            var height = width * aspect;
            if (maxHeight > 0 && height > maxHeight) {
                height = maxHeight;
                width = height / aspect;
            }
            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = height;
        }

        // RawImage has no preserveAspect, so when the parent column is resized (panel
        // resize / window resize) we must re-letterbox or the video stretches/skews.
        // Guard on the column width actually changing to avoid an infinite rebuild loop
        // (Calculate -> preferredWidth/Height change -> our rect changes -> this fires
        // again).
        private void OnRectTransformDimensionsChange() {
            if (player == null || player.clip == null || parentRect == null) {
                return;
            }
            var colWidth = parentRect.rect.width - parentPadding.left - parentPadding.right;
            if (Mathf.Approximately(colWidth, lastColWidth)) {
                return;
            }
            Calculate();
        }

        private void ReleaseRT() {
            if (rt != null) {
                player.Stop();
                player.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                rt = null;
            }
        }

        public void SetInteractable(bool yes) {
            button.interactable = yes;
        }

        public void SetOnClick(Action act) {
            onClick = act;
        }

        protected void OnClick() {
            if (onClick == null) {
                D.Err("OnClick not rigged for {0} -> {1}", this, D.GOPath(transform));
            } else {
                onClick.Invoke();
            }
        }

        // RenderTextures aren't reset by the pool's plain SetActive(false) -- release them
        // here so a despawned-but-not-destroyed video block doesn't keep a GPU texture and
        // a playing VideoPlayer alive while sitting inactive in the pool.
        public void PoolDeactivate() {
            ReleaseRT();
            gameObject.SetActiveFast(false);
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public override string ToString() {
            return "WVideoBlockAutoSize[]";
        }
    }
}
