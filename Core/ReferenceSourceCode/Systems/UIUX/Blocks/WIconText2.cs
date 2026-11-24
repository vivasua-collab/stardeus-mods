using Game.Constants;
using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class WIconText2 : WIconText {
        [SerializeField] protected TMP_Text text2;
        [SerializeField] protected UITextCollisionResolver resolver;
        public TMP_Text Text2 => text2;
        protected string lastText2 = ".";

        public void Load(Sprite sprite, string text, string textSide = null) {
            SetSprite(sprite);
            SetTint(Consts.ColorWhite);
            SetText(text, autoResize: false);
            SetText2(textSide, autoResize: false);
            if (resolver != null) {
                resolver.AutoResize();
            }
        }
        public void Load(string icon, string text, string textSide = null) {
            SetImage(icon);
            SetTint(Consts.ColorWhite);
            SetText(text, autoResize: false);
            SetText2(textSide, autoResize: false);
            if (resolver != null) {
                resolver.AutoResize();
            }
        }

        public void SetText(string text, bool autoResize = true) {
            if (lastText == text) {
                return;
            }
            lastText = text;
            textMain.text = text;
            if (autoResize) {
                resolver.AutoResize();
            }
        }

        public void SetText2(string txt, bool autoResize = true, bool disableOnBlank = true) {
            if (UIUtils.SetText(text2, txt, ref lastText2, disableOnBlank) && autoResize) {
                resolver.AutoResize();
            }
        }
        public override string ToString() {
            return $"WIconTextText[{lastIcon}|{lastText}|{lastText2}]";
        }
    }
}
