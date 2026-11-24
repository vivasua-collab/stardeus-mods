using System;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Constants {
    public class ColorDef {
        public string Id;
        private string nameT;
        public string NameT => nameT ??= NameColorId == null
            ? $"color.{Id}".T()
            : NameNumber > 0
                ? $"{$"color.{NameColorId}".T()} {NameNumber}"
                : $"color.{NameColorId}".T();
        /// <summary>
        /// Shades of one base color are named "{base color name} {NameNumber}" (Grey 1, Grey 2),
        /// so a palette of shades needs no translation per shade. Without a NameNumber the
        /// shade takes the base color name as is
        /// </summary>
        public string NameColorId;
        public int NameNumber;
        public bool IsGeneric;
        public string SpriteId;
        public string Hex;
        public float Alpha;
        public int[] Data;
        public Color Color;
        public string[] Groups;
        public bool SkipAtlas;
        public string AliasFor;
        /// <summary>
        /// How likely this color is going to show up. 1 = default; 0.1 = rare
        /// </summary>
        public float Weight;
        public bool IsBroken;
    }
}
