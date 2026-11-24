using System;
using Game.Components;
using Game.Constants;
using KL.Grid;
using KL.Text;
using KL.Utils;
using UnityEngine;

namespace Game.Data {
    public class InfoText : InfoItem {
        public override InfoItemType Type => InfoItemType.Text;
        public override string Renderer => null;
        public override int RendererHash => 0;
        public override int RendererShardSize => 0;
        public override bool RendererSorted => false;
        public override int TypeInt => 1;
        public override int RendererSortOrder => 0;
        private float FontSize = 4f;
        private string Text;
        public override string Info => InfoKey;
        private Vector2 TextOffset;
        private GameTextTMP gameText;

        public override bool IsBlank => Text == null;

        public override string ToString() {
            return $"InfoItem [{Entity.DefinitionId}@{Entity.Id}/{Pos.String(Entity.PosIdx)}][{Component.GetName()}][Text: {Text}]";
        }

        public static InfoText For(IComponent comp) {
            var i = new InfoText {
                Entity = comp.Entity,
                Component = comp,
            };
            if (comp.Entity is Being b) {
                i.Being = b;
            }
            return i;
        }

        public bool UpdateText(string text, string hoverText) {
            return UpdateText(text, hoverText, Consts.V2Zero);
        }

        public bool UpdateText(string text, string hoverText, Vector2 offset) {
            if (Text != text) {
                Text = text;
                TextOffset = offset;
                InfoKey = hoverText;
                WasUpdated = true;
                return true;
            }
            return false;
        }

        public override void Hide() {
            if (!IsShowing) { return; }
            IsShowing = false;
            if (gameText == null) {
                D.Err("Showing text but GameText is null!");
                return;
            }
            gameText.Destroy();
            gameText = null;
            Text = null;
        }
        public override void Clear() {
            Hide();
        }

        public override void Show() {
            if (!WasUpdated) { return; }
            WasUpdated = false;
            if (!IsShowing && Text != null) {
                if (gameText == null) {
                    gameText = GameTextTMP.Create("InfoText", Text,
                        Entity.Position + TextOffset);
                    gameText.SetFontSize(FontSize);
                }
                gameText.SetText(Text);
                IsShowing = true;
            } else if (Text == null) {
                Hide();
            } else { // IsShowing && Text != null
                gameText.MoveTo(Entity.Position + TextOffset);
                gameText.SetText(Text);
            }
        }

        public override void Move(Vector2 pos) {
            if (gameText == null || Entity == null) { return; }
            //D.Err("Moving InfoText for {0}", Entity);
            gameText.MoveTo(Entity.Position + TextOffset);
        }

        public override void MoveBy(float dx, float dy) {
            if (gameText == null || Entity == null) { return; }
            //D.Err("Moving InfoText for {0}", Entity);
            gameText.MoveBy(dx, dy);
        }

        public override string DebugInfo {
            get {
                return $"InfoText: Text={Text} Key={InfoKey} IsShowing={IsShowing} GameText={(gameText != null ? gameText.Text : "null")}";
            }
        }
    }
}
