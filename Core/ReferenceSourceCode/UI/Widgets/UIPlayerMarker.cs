using System;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Rendering;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.UI {
    /// <summary>
    /// Shows a circle around the player in Starmap (region / universe view)
    /// </summary>
    public sealed class UIPlayerMarker : MonoBehaviour, ICustomPoolable {
        private UICircle circle;
        private RectTransform rekt;
        private UIFixedScaleOnZoom scaler;
        [SerializeField] private Transform scaleParent;
        private float pulseAmplitude;
        private Color colorHyperspace;
        private Color colorRegular;
        private WImage linePrefab;
        private WImage targetLine;
        private SpaceLink displayedLink;
        private Lifetime lifetime;
        private float lastUIScale;
        public float HyperspaceMoveProgress;
        public void SetColor(Color color) {
            circle.SetColor(color);
        }

        public void SetPulse(float amplitude) {
            if (amplitude == 0f && pulseAmplitude != 0f) {
                // scaler.SetUnscaledSize(1f);
                scaleParent.localScale = Consts.V3One;
            }
            pulseAmplitude = amplitude;
        }

        public void Load(UISpaceObject location) {
            if (location == null) {
                gameObject.SetActiveFast(false);
                return;
            }
            if (rekt == null) {
                rekt ??= GetComponent<RectTransform>();
                circle ??= GetComponent<UICircle>();
                colorRegular = The.UITheme.GetColor("StarmapMarker", this);
                colorHyperspace = The.UITheme.GetColor("StarmapMarker_Hyperspace", this);
            }
            UIRect.CopyRT(location.Rekt, rekt);
            circle.Load(100f, 4f, 2.5f,
                false, A.IsInHyperspace ? colorHyperspace : colorRegular,
                    constantThickness: false);
            scaler ??= GetComponent<UIFixedScaleOnZoom>();
            scaler.ResetScale();
            gameObject.SetActiveFast(true);
            // scaler.SetUnscaledSize(1f);
        }

        public void HidePath() {
            displayedLink = null;
            HideTargetLine();
        }

        private void HideTargetLine() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = null;
            // target line gets disposed automatically with lifetime
            if (targetLine != null && !targetLine.IsDisposed) {
                D.Err("Lifetime was disposed but target line isn't");
                targetLine.Dispose();
            }
            targetLine = null;
        }

        public void ShowLink(Lifetime life, SpaceLink link) {
            lastUIScale = -1f;
            displayedLink = link;
            if (link == null) {
                HidePath();
                return;
            }
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = life.CreateChild("UIPlayerMarker");
            linePrefab ??= ResourceLibrary.FindPrefab<WImage>(
                UIPrefabId.UILineDashedAnimated);
            UpdateLinkDisplay(link);
        }

        private float lastMoveProg;

        private void UpdateLinkDisplay(SpaceLink link) {
            if (link == null) { return; }
            if (!UIShowing.Starmap) { return; }
            if (rekt?.parent == null) { return; }
            var scale = rekt.parent.localScale.x;
            var sf = link.To?.SO?.UI?.ScaleFactor ?? link.From?.SO?.UI?.ScaleFactor;
            if (sf.HasValue) {
                scale *= sf.Value;
            }
            if (Maths.Approximately(lastUIScale, scale)
                    && Maths.Approximately(lastMoveProg, HyperspaceMoveProgress)) {
                return;
            }
            lastUIScale = scale;
            lastMoveProg = HyperspaceMoveProgress;
            // which end is us?
            ISpaceEntity from;
            ISpaceEntity to;
            if (link.From == A.StarmapCtx.PlayerLocationInChildren.SpaceEntity) {
                from = link.To;
                to = link.From;
            } else {
                from = link.From;
                to = link.To;
            }
            var fp = from.Position;
            var tp = to.Position;
            var dir = fp - tp;
            dir *= scale * (1f - HyperspaceMoveProgress);
            if (dir.sqrMagnitude < 200f * 200f) {
                HideTargetLine();
                return;
            }

            if (lifetime == null) {
                D.ErrSoft("Trying to update link display without lifetime");
                return;
            }

            var start = Vector2.MoveTowards(Consts.V2Zero, dir, 100);
            var end = Vector2.MoveTowards(dir, Consts.V2Zero, 100);

            if (targetLine == null || !targetLine.gameObject.activeSelf) {
                targetLine = UIDraw.SpawnLineTL(lifetime, linePrefab, rekt, 32, start, end);
                targetLine.name = "TargetLine";
                targetLine.Rect.SetAsFirstSibling();
            } else {
                UIDraw.UpdateLineTL(32, start, end, targetLine.Rect);
            }
            var nextJumpTarget = A.S.Query.GetNextHyperjumpTarget.Ask();
            if (A.IsInHyperspace || link.Contains(nextJumpTarget)) {
                targetLine.SetTint(colorHyperspace);
            } else {
                targetLine.SetTint(colorRegular);
            }
        }

        private void Update() {
            if (pulseAmplitude > 0f) {
                var s = 1f + RenderUtils.SinUnscaledTime5 * pulseAmplitude;
                // scaler.SetUnscaledSize(s);
                scaleParent.localScale = new Vector3(s, s, s);
            }
            if (displayedLink != null) {
                UpdateLinkDisplay(displayedLink);
            }
        }

        public void MoveTo(Vector2 pos) {
            rekt.localPosition = pos;
            if (displayedLink != null) {
                UpdateLinkDisplay(displayedLink);
            }
        }

        public void SetInHyperspace(bool isInHyperspace) {
            if (isInHyperspace) {
                SetColor(colorHyperspace);
                SetPulse(0.1f);
            } else {
                SetColor(colorRegular);
                SetPulse(0f);
            }
        }

        public void ForceUpdate() {
            circle.ForceUpdate();
        }

        public void PoolDeactivate() {
            HideTargetLine();
            gameObject.SetActiveFast(false);
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }
    }
}
