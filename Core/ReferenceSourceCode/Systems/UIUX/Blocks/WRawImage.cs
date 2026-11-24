using System;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class WRawImage : WBase {
        [SerializeField] private RawImage image;

        public void Load(Texture2D tex) {
            SetTexture(tex);
        }

        public void Load(Texture2D tex, Rect uvRect) {
            if (tex == null) {
                image.texture = null;
                image.enabled = false;
            } else {
                image.texture = tex;
                image.uvRect = uvRect;
                image.enabled = true;
            }
        }

        public void Load(RenderTexture tex) {
            if (tex == null) {
                image.texture = null;
                image.enabled = false;
            } else {
                image.texture = tex;
                image.uvRect = new Rect(0, 0, 1, 1);
                image.enabled = true;
            }
        }

        public void SetTexture(Texture2D tex) {
            if (tex == null) {
                image.texture = null;
                image.enabled = false;
            } else {
                image.texture = tex;
                image.uvRect = new Rect(0, 0, 1, 1);
                image.enabled = true;
            }
        }

        public void SetSprite(string imageId) {
            D.Warn("Setting sprite: {0}", imageId);
            SetSprite(Images.Sprite(imageId));
        }

        public void SetSprite(Sprite sprite) {
            if (sprite == null) {
                image.texture = null;
                image.enabled = false;
            } else {
                image.texture = sprite.texture;
                image.uvRect = new Rect(
                    sprite.rect.x / sprite.texture.width,
                    sprite.rect.y / sprite.texture.height,
                    sprite.rect.width / sprite.texture.width,
                    sprite.rect.height / sprite.texture.height);
                image.enabled = true;
            }
        }
    }
}
