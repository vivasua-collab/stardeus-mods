using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonToolWithSubmenu : WButtonSubmenu {
        [SerializeField] private string activeIconColorId;
        private ITool tool;
        private bool isActive;
        private GameState S;
        public void Load(GameState s, ITool tool, WBottomBarButton subButtonPrefab) {
            this.tool = tool;
            S = s;
            name = $"ToolButton_{tool.Id}_WithSub";
            Load(tool.MenuIcon, tool.NameT);

            s.Sig.ToolChanged.AddListener(OnToolChange, gameObject);
            foreach (var param in tool.AvailableParams) {
                var btn = UISpawn.WidgetPermanent(subButtonPrefab, Container);
                var childTool = S.Toolbox.FindChildTool(tool, param);
                string glyph;
                if (childTool != null) {
                    btn.Item.Load(childTool.MenuIcon, Tool.NameWithShortcut(childTool, out glyph), () => {
                        ToggleTool(childTool);
                    });
                } else {
                    btn.Item.Load(tool.MenuIconFor(param), Tool.NameWithShortcut(tool, out glyph, param),
                        () => OnClickSub(param));
                }
                btn.SetShortcutGlyph(glyph);
            }
        }
        private void ToggleTool(ITool tool) {
            if (The.Toolbox.ActiveTool == tool) {
                The.Toolbox.Clear();
                Deactivate();
            } else {
                The.Toolbox.SetActive(tool);
            }
        }

        private void OnClickSub(string param) {
            S.Toolbox.SetActive(tool, param);
            HideSubmenu();
            SetImage(tool.MenuIconFor(param));
        }

        protected override void OnClick() {
            if (isActive) {
                S.Toolbox.Clear();
            } else {
                base.OnClick();
            }
        }

        private void OnToolChange(ITool tool) {
            var thisTool = this.tool;
            var isChildTool = tool.ParentId == thisTool.Id;

            if (!isChildTool && tool != thisTool) {
                Deactivate();
                return;
            }

            isActive = true;
            if (isChildTool) {
                HideSubmenu();
                SetImage(tool.MenuIcon);
            } else {
                if (tool.CurrentParam != null) {
                    SetImage(tool.MenuIconFor(tool.CurrentParam));
                } else {
                    SetImage(tool.MenuIcon);
                }
            }
            SetTint(The.UITheme.GetColor(activeIconColorId, this));
        }

        private void Deactivate() {
            if (isActive) {
                isActive = false;
                SetTint(Consts.ColorWhite);
                SetImage(tool.MenuIcon);
            }
        }

        public override string ToString() {
            return $"WButtonTool[{tool.Id}]";
        }
    }
}
