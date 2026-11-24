using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    // A research as the tree shows it zoomed out: a solid plate, so lines end under it
    // instead of running through its name, and a row of what it unlocks, so it says what it
    // gives without opening its card. The card (WResearchNode) shows on hover, or in place of
    // the tile when zoomed in.
    [DisallowMultipleComponent]
    public class WResearchTile : WBase {
        // As many unlock icons as fit the tile's width, with the "+N" taking the last one's place
        private const int MaxUnlockIcons = 3;
        [SerializeField] private Image plate;
        [SerializeField] private GameObject outline;
        [SerializeField] private CanvasGroup outlineCG;
        [SerializeField] private WImage[] unlockIcons;
        [SerializeField] private WText moreUnlocks;
        [SerializeField] private WImage bookmarkIcon;
        override protected bool IsPoolable => false;
        private readonly List<Sprite> unlockSprites = new();
        private Def def;
        private ResearchProgress progress;
        // The outline keeps one thickness on screen at any zoom: scaled with the tree, its border
        // shrank to a fraction of a pixel when zoomed out, and shimmered or vanished.
        // Screen width of the sprite's slice: its 2 px line sits in a 6 px slice, so about 3 px.
        private const float OutlineScreenWidth = 9f;
        private bool isFlashing;
        private float baseFontSizeMax;
        private float zoom = 1f;
        private Image outlineImage;

        public int DebugUnlockIconsShown {
            get {
                var cnt = 0;
                foreach (var unlockIcon in unlockIcons) {
                    if (unlockIcon.gameObject.activeSelf) { cnt++; }
                }
                return cnt;
            }
        }

        public void Load(Def def, ResearchProgress progress, TMP_Text title) {
            this.def = def;
            this.progress = progress;
            FitLongestWord(title, def.NameT);
            bookmarkIcon.Load(IconId.CBookmark);
            // What a research unlocks never changes in a session, and finding it scans every def
            unlockSprites.Clear();
            foreach (var unl in A.S.Research.GetUnlockableIconsFor(def)) {
                if (unl.def != null) {
                    unlockSprites.Add(unl.def.PreviewSprite);
                } else if (unl.unlockEff != null) {
                    unlockSprites.Add(Images.Sprite(unl.unlockEff.Icon));
                }
            }
            Repaint();
        }

        // Autosizing only shrinks a name that runs out of lines. A single word wider than the tile
        // ("Biochemical") would run off it instead, so the largest size is capped to fit that word.
        private void FitLongestWord(TMP_Text title, string name) {
            if (baseFontSizeMax <= 0) {
                baseFontSizeMax = title.fontSizeMax;
            }
            var width = title.rectTransform.rect.width - title.margin.x - title.margin.z;
            var size = title.fontSize;
            title.fontSize = baseFontSizeMax;
            var widest = 0f;
            foreach (var word in name.Split(' ')) {
                widest = Mathf.Max(widest, title.GetPreferredValues(word).x);
            }
            title.fontSize = size;
            var max = baseFontSizeMax;
            if (width > 0 && widest > width) {
                max = Mathf.Max(title.fontSizeMin, Mathf.Floor(baseFontSizeMax * width / widest));
            }
            title.fontSizeMax = max;
        }

        // Tiles live for the whole session, so this has to be a pure function of current state
        public void Repaint() {
            if (def == null) { return; }
            var isDiscovered = A.S.Research.IsDiscoveredOrSandbox(def.IdH);
            plate.color = WResearchNode.BackgroundColorFor(progress, isDiscovered);
            var isMarked = isDiscovered && !progress.IsUnlocked && progress.IsBookmarked;
            bookmarkIcon.SetActiveFast(isMarked);
            if (!isFlashing) {
                SetOutline(isMarked);
            }
            RepaintUnlocks(isDiscovered);
        }

        private void RepaintUnlocks(bool isDiscovered) {
            var count = isDiscovered ? unlockSprites.Count : 0;
            var slots = Mathf.Min(MaxUnlockIcons, unlockIcons.Length);
            // The last slot turns into "+N" when there is more than fits
            var shown = count > slots ? slots - 1 : count;
            var isHidden = !def.IsInSeason;
            for (int i = 0; i < unlockIcons.Length; i++) {
                var slot = unlockIcons[i];
                var isOn = i < shown;
                slot.SetActiveFast(isOn);
                if (isOn) {
                    slot.SetSprite(isHidden ? Images.Sprite(IconId.CQuestion) : unlockSprites[i]);
                }
            }
            var isMore = count > slots;
            moreUnlocks.SetActiveFast(isMore);
            if (isMore) {
                moreUnlocks.SetText($"+{count - shown}");
            }
        }

        public void SetZoom(float zoom) {
            this.zoom = zoom;
            if (outline.activeSelf) {
                UpdateOutlineWidth();
            }
        }

        private void SetOutline(bool on) {
            outline.SetActiveFast(on);
            if (on) {
                UpdateOutlineWidth();
            }
        }

        // A sliced border is sprite border / (pixels per unit * multiplier) canvas units wide
        private void UpdateOutlineWidth() {
            outlineImage ??= outline.GetComponent<Image>();
            var border = outlineImage.sprite != null ? outlineImage.sprite.border.x : 0f;
            if (border <= 0f) { return; }
            var mul = border * zoom / (outlineImage.pixelsPerUnit * OutlineScreenWidth);
            outlineImage.pixelsPerUnitMultiplier = Mathf.Max(0.01f, mul);
        }

        public void Flash() {
            Coroutines.StartDirect(FlashOutline());
        }

        private IEnumerator FlashOutline() {
            isFlashing = true;
            SetOutline(true);
            outlineCG.alpha = 0f;
            const float fadeDuration = 0.25f;
            var s = A.S;
            for (int i = 0; i < 2; i++) {
                while (outlineCG.alpha < 1f) {
                    outlineCG.alpha += Time.unscaledDeltaTime / fadeDuration;
                    yield return null;
                    if (!GameState.IsCurrent(s)) { yield break; }
                }
                while (outlineCG.alpha > 0f) {
                    outlineCG.alpha -= Time.unscaledDeltaTime / fadeDuration;
                    yield return null;
                    if (!GameState.IsCurrent(s)) { yield break; }
                }
            }
            outlineCG.alpha = 1f;
            isFlashing = false;
            Repaint();
        }
    }
}
