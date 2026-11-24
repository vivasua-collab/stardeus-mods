using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WIconText : WBase {
        [SerializeField] protected Image image;
        [SerializeField] protected TMP_Text textMain;
        public Image Image => image;
        public TMP_Text Text => textMain;
        protected string lastIcon = ".";
        protected string lastText = ".";

        public void Load(Sprite sprite, string text) {
            SetSprite(sprite);
            SetText(text);
            SetTint(Consts.ColorWhite);
        }
        public void Load(string icon, string text) {
            SetImage(icon);
            SetText(text);
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
        public void SetImage(string icon) {
            if (lastIcon == icon) {
                return;
            }
            lastIcon = icon;
            if (icon == null) {
                image.sprite = null;
                return;
            }
            if (!Images.TrySprite(icon, out var sprite)) {
                D.Err("Trying to set unavailable sprite on WIconText: {0} -> {1} | {2}",
                    icon, this, D.GOPath(transform));
                return;
            }
            image.sprite = sprite;
        }

        public bool SetText(string text) {
            return UIUtils.SetText(textMain, text, ref lastText, false);
        }
        public override string ToString() {
            return $"WIconText[{lastIcon}|{lastText}]";
        }
    }
}
