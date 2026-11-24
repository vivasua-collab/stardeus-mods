using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeImage {
        public static readonly UIThemeImage WhiteSquare = new UIThemeImage() {
            Color = Color.white,
        };

        public Color Color { get; set; }
        public Sprite Sprite { get; set; }
        public bool ColorOnly { get; set; }
        public float PPUMul { get; set; }
        public Image.Type Type { get; set; }

        public void ApplyTo(Image image) {
            if (!ColorOnly) {
                image.sprite = Sprite;
                if (PPUMul > 0f) {
                    image.pixelsPerUnitMultiplier = PPUMul;
                }
                image.type = Type;
            }
            image.color = Color;
        }
    }
}
