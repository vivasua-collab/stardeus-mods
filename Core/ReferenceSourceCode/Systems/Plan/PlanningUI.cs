using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.Tools;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Plan {
    public class PlanningUI {
        private readonly GameState S;
        private Lifetime lifetime;
        public PlanningUI(GameState s) {
            S = s;
        }

        private Vector2? planWidgetPos;
        private Vector2 planWidgetSize;
        private WPanel widget;
        public void Show() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("PlanningUI");
            widget = UISpawn.Widget<WPanel>(lifetime, UIPrefabId.WListChoicePopup, UIPoolRoot.PopupContainer, 0);
            widget.Rect.SetAsLastSibling();
            widget.Load(T.OverlayTipPlan, () => {
                S.Sig.HideTargetOverlay.Send(OverlayId.Plan);
                Close();
            });
            var tool = The.Toolbox.Get<ToolPlan>(ToolPlan.ToolId);
            int bIdx = 0;
            int idx = 0;


            var block = UISpawn.Block(lifetime, widget.Content, bIdx++);

            idx = 0;

            var drawModeHeader = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            drawModeHeader.Load(T.NounDrawMode);

            foreach (var param in tool.ModeParams) {
                idx = AddToolBtn(block, tool, idx, param);
            }

            block = UISpawn.Block(lifetime, widget.Content, bIdx++);
            idx = 0;

            var toolsHeader = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            toolsHeader.Load(T.InputGroupTools);

            foreach (var param in tool.ExtraToolParams) {
                idx = AddToolBtn(block, tool, idx, param);
            }

            PlanManagementBlock.Load(lifetime, widget, S, bIdx++);

            LoadFooter(tool);

            if (planWidgetPos.HasValue) {
                widget.Rect.anchoredPosition = planWidgetPos.Value;
                widget.Rect.sizeDelta = planWidgetSize;
            } else {
                UIUtils.AfterShowPopup(widget.Rect, true);
            }
        }

        private void LoadFooter(ToolPlan tool) {
            int footIdx = 0;

            var colorChoice = UISpawn.Widget<WButtonIconText2>(lifetime, UIPrefabId.WContent4Button, widget.Footer.Container, footIdx++);
            colorChoice.Load(IconId.WSquare, T.NounColor, () => {
                var qc = QuickChoice.Create();
                var sprite = Images.Sprite(IconId.WSquare);
                qc.Widget = colorChoice.Rect;
                for (int i = 1; i <= 5; i++) {
                    var colNum = Units.Num(i);
                    var col = ToolPlan.GetColor(i);
                    var iconBtn = new QuickChoice.IconButton {
                        Icon = sprite,
                        Tint = col,
                        AutoClose = true,
                        OnClick = () => tool.Configure(colNum)
                    };
                    var name = tool.MenuNameFor(colNum).T();
                    qc.AddIconButton(name, iconBtn);
                }
                S.UIQuery.ShowQuickChoice.Ask(qc);
            });
            tool.SelectedColorB.Bind(lifetime, (c) => {
                colorChoice.SetTint(c);
            });


            var erasePlanBtn = UISpawn.Widget<WButtonIconText>(lifetime, UIPrefabId.WPanelButtonIcon, widget.Footer.Container, footIdx++);
            erasePlanBtn.Load(IconId.WPlanningClear, T.ErasePlan, () => {
                var qc = QuickChoice.Create();
                qc.Widget = erasePlanBtn.Rect;
                qc.AddButton(T.Confirm, () => PlanManagementBlock.ErasePlan(S));
                S.UIQuery.ShowQuickChoice.Ask(qc);
            });
            widget.ShowFooter(true);

            var mirrorX = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WContent4Toggle, widget.Footer.Container, footIdx++);
            mirrorX.Load(T.ToolPlanMirror_X, false, (y) => {
                tool.Configure("mirror_x");
            });
            UIUtils.AddTip(lifetime, mirrorX, T.ToolPlanMirror_XDesc);
            tool.MirrorXB.Bind(lifetime, (y) => {
                mirrorX.SetValue(y, false);
            });

            var mirrorY = UISpawn.Widget<WToggleText>(lifetime, UIPrefabId.WContent4Toggle, widget.Footer.Container, footIdx++);
            mirrorY.Load(T.ToolPlanMirror_Y, false, (y) => {
                tool.Configure("mirror_y");
            });
            UIUtils.AddTip(lifetime, mirrorY, T.ToolPlanMirror_YDesc);
            tool.MirrorYB.Bind(lifetime, (y) => {
                mirrorY.SetValue(y, false);
            });

        }

        private int AddToolBtn(WContentBlock block, ToolPlan tool, int idx, string param) {
            var btn = UISpawn.Widget<WButtonIconText2>(lifetime, UIPrefabId.WContent2Button, block.Container, idx++);
            var nameKey = tool.MenuNameFor(param);
            var name = nameKey.T();
            btn.Load(tool.MenuIconFor(param), name, () => {
                S.Toolbox.SetActive(tool, param);
            });
            var itemPar = param;
            tool.ModeB.Bind(lifetime, p => {
                if (p == itemPar) {
                    btn.SetText($"{name} {Texts.Accented(GlyphMap.TriangleLeft)}");
                } else {
                    btn.SetText(name);
                }
            });
            var descKey = $"{nameKey}.desc";
            if (descKey.HasT()) {
                UIUtils.AddTip(lifetime, btn, descKey.T());
            }

            return idx;
        }

        public void Close() {
            if (widget != null) {
                planWidgetPos = widget.Rect.anchoredPosition;
                planWidgetSize = widget.Rect.sizeDelta;
            }
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }
    }
}
