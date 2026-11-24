using System;
using System.Collections.Generic;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class UIThemeImageDef {
        public float Alpha { get; set; }
        public string Color { get; set; }
        public string Image { get; set; }
        public string ImageType { get; set; }
        public float ImagePPUMultiplier { get; set; }
        public int[] ImageBorderLBRT { get; set; }
        private Sprite cachedSprite;
        private string cachedSpriteId;
        public Sprite GetSprite(string spriteId, out bool keep) {
            keep = Image == "-";
            if (keep || Image == null) { return null; }
            if (cachedSprite != null) {
                if (cachedSpriteId != spriteId) {
                    D.Err("Cached sprite id mismatch! {0} != {1}",
                        cachedSpriteId, spriteId);
                } else {
                    return cachedSprite;
                }
            }
            cachedSpriteId = spriteId;
            var id = Image;
            var borders = ImageBorderLBRT;
            if (Arrays.IsEmpty(borders)) {
                cachedSprite = Images.Sprite(id);
                return cachedSprite;
            }
            if (borders.Length != 4) {
                D.Err("Passing incorrect number of borders for sprite: {0}: {1}",
                    id, D.Dump(borders));
                cachedSprite = Images.Sprite(id);
                return cachedSprite;
            }

            var b = new int4(borders[0], borders[1], borders[2], borders[3]);
            cachedSprite = Images.SpriteWithBorders(id, b, spriteId);
            return cachedSprite;
        }

        public void SetImage(UITheme theme, Dictionary<string, UIThemeImage> images, string id) {
            var img = new UIThemeImage();
            img.Color = theme.GetColor(Color, Alpha, this);
            var sprite = GetSprite(id, out var keep);
            if (keep) {
                img.ColorOnly = true;
            } else {
                img.Sprite = sprite;
                img.PPUMul = ImagePPUMultiplier;
                if (string.IsNullOrWhiteSpace(ImageType)) {
                    img.Type = UnityEngine.UI.Image.Type.Simple;
                } else {
                    if (Enum.TryParse<Image.Type>(ImageType, out var type)) {
                        img.Type = type;
                    } else {
                        D.Err("Invalid ImageType in theme {0} Image {1}.  Valid types: Simple, Sliced, Filled, Tiled",
                            theme.Id, ImageType);
                    }
                }
            }
            if (!images.TryAdd(id, img)) {
                D.Err("UI Theme image already exists by id: {0}", id);
            }
        }
    }
}
