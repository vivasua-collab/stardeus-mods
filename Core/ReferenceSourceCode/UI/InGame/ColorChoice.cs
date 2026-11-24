using System;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using UnityEngine;

namespace Game.UI {
    public class ColorChoice {
        private Action<ColorDef> onChoose;
        private string groupId;
        private bool includeRandom;
        private ColorChoice() { }

        public static ColorChoice ForGroup(GameState s, string groupId) {
            _ = s;
            var cc = new ColorChoice();
            cc.groupId = groupId;
            return cc;
        }

        public static ColorChoice ForGeneric(GameState s) {
            return ForGroup(s, null);
        }

        public ColorChoice WithAction(Action<ColorDef> onChoose) {
            this.onChoose = onChoose;
            return this;
        }

        public ColorChoice IncludeRandom(bool rand) {
            includeRandom = rand;
            return this;
        }

        public void Show(RectTransform forRect) {
            var colors = groupId == null ? Palette.Generic : Palette.ForGroup(groupId);
            var qc = QuickChoice.Create(T.ChooseColor);
            qc.Widget = forRect;

            if (includeRandom) {
                qc.AddIconButton(IconId.CColorWheel, T.RandomColor, () => {
                    onChoose(null);
                });
            }

            foreach (var c in colors) {
                var def = c;
                qc.AddIconButton(Images.Sprite(def.SpriteId), def.NameT, () => {
                    onChoose(def);
                });
            }

            qc.Show();
        }
    }
}
