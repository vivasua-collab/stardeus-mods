using System;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonTool : WButtonIconText {
        [SerializeField] private string activeIconColorId;
        [SerializeField] private WText ShortcutGlyph;
        private ITool tool;
        private bool isActive;
        public void Load(GameState s, ITool tool) {
            this.tool = tool;
            name = $"ToolButton_{tool.Id}";
            var tname = Tool.NameWithShortcut(tool, out var glyph);
            Load(tool.MenuIcon, tname, ToggleTool);
            if (glyph != null) {
                ShortcutGlyph.Load(glyph);
                ShortcutGlyph.SetActiveFast(true);
            } else {
                ShortcutGlyph.SetActiveFast(false);
            }
            s.Sig.ToolChanged.AddListener(OnToolChange, gameObject);
        }

        private void OnToolChange(ITool tool) {
            if (tool != this.tool) {
                Deactivate();
            } else {
                if (!isActive) {
                    isActive = true;
                    SetTint(The.UITheme.GetColor(activeIconColorId, this));
                }
            }
        }

        private void ToggleTool() {
            if (The.Toolbox.ActiveTool == tool) {
                The.Toolbox.Clear();
                Deactivate();
            } else {
                The.Toolbox.SetActive(tool);
            }
        }

        private void Deactivate() {
            if (isActive) {
                isActive = false;
                SetTint(Consts.ColorWhite);
            }
        }

        public override string ToString() {
            return $"WButtonTool[{tool.Id}]";
        }
    }
}
