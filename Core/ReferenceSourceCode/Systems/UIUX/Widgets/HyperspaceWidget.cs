using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class HyperspaceWidget : BaseWidget, IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private Image image;
        [SerializeField] private Image overlayIcon;
        [SerializeField] private GameObject overlay;
        private Material material;
        [SerializeField] private WButton button;
        private Func<string> tipFunc;
        private bool isHovering;

        public void Initialize(Action onClick, Func<string> tipFunc) {
            Register(UIWidgetId.Hyperspace, this);
            material = image.material = new Material(image.material);
            button.Load(onClick);
            this.tipFunc = tipFunc;
            SetLooks();
        }

        public void ShowOverlay(string icon, Color color) {
            overlayIcon.sprite = Images.Sprite(icon);
            overlayIcon.color = color;
            overlay.SetActiveFast(true);
        }

        public void HideOverlay() {
            overlay.SetActiveFast(false);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            isHovering = true;
            The.SysSig.ShowDynamicTooltip.Send(tipFunc, true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isHovering) {
                isHovering = false;
                The.SysSig.HideTooltip.Send();
            }
        }

        public void SetLooks() {
            var th = The.UITheme;
            material.SetColor("_BackgroundColor", th.GetColor("HyperspaceProgressBG", this));
            material.SetColor("_BarBgColor", th.GetColor("HyperspaceProgressBarBG", this));
            material.SetColor("_BarFillColor", th.GetColor("HyperspaceProgressBarFill", this));
            material.SetColor("_StripeColor", th.GetColor("HyperspaceProgressStripe", this));

            var rt = image.rectTransform;
            material.SetFloat("_ImageAspect", rt.rect.width / rt.rect.height);
            material.SetFloat("_ImageHeightPx", rt.rect.height);
        }

        public void SetProgress(float progress) {
            material.SetFloat(ShaderVars._Progress, progress);
        }

        public void SetStripes(float density, float speed) {
            material.SetFloat(ShaderVars._StripeDensity, density);
            material.SetFloat(ShaderVars._StripeBaseSpeed, speed);
        }
    }
}
