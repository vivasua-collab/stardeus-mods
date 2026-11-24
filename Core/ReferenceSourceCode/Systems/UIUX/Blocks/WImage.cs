using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WImage : WBase {
        [SerializeField] protected Image image;
        protected string lastIcon = ".";

        public Material Material {
            get => image.material;
            set => image.material = value;
        }

        public void Load(Sprite sprite) {
            SetSprite(sprite);
            SetTint(Consts.ColorWhite);
        }
        public void Load(string icon) {
            SetImage(icon);
            SetTint(Consts.ColorWhite);
        }
        public void SetSprite(Sprite sprite) {
            lastIcon = null;
            image.sprite = sprite;
            image.color = Consts.ColorWhite;
        }

        public void SetTint(Color tint) {
            image.color = tint;
        }
        public void SetRaycastable(bool yes) {
            image.raycastTarget = yes;
        }
        public void SetImage(string icon) {
            if (lastIcon == icon) {
                return;
            }
            lastIcon = icon;
            if (!Images.TrySprite(icon, out var sprite)) {
                D.Err("Trying to set unavailable sprite on WImage: {0} -> {1} | {2}",
                    icon, this, D.GOPath(transform));
                return;
            }
            image.sprite = sprite;
        }
        public override string ToString() {
            return $"WImage[{lastIcon}]";
        }
    }
}
