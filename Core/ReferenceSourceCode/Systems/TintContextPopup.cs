using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems {
    public sealed class TintContextPopup : IOverlayLegendPopup {
        private static readonly int popupHash = Hashes.S("TintOverlay");
        public int UniquePopupHash => popupHash;
        public bool ShowInScreenCenter => true;

        private readonly GameState S;
        private readonly TintSys sys;
        private bool isOpen;

        public bool IsShowing => levelRWidget != null;

        // Widget references for live pigment level updates
        private WIconText2Progress levelRWidget;
        private WIconText2Progress levelGWidget;
        private WIconText2Progress levelBWidget;
        private WIconText2 estimateWidget;

        // Footer widget references
        private WButtonIconText2 colorFooterWidget;
        private WDropdown paintDropdown;

        private static readonly List<string> paintModeOptions = new() {
            T.PaintFloor, T.PaintWalls, T.EraseFloor, T.EraseWalls,
        };

        public TintContextPopup(GameState s, TintSys sys) {
            S = s;
            this.sys = sys;
        }

        public void Show() => S.UIQuery.ShowContextPopup.Ask(this, true);
        public void Hide() => S.UIQuery.ShowContextPopup.Ask(this, false);

        public void Refresh() {
            if (isOpen) Show();
        }

        public void OnPopupOpened() => isOpen = true;

        public void OnPopupClosed() {
            isOpen = false;
            ClearWidgetRefs();
            if (The.Toolbox.ActiveTool.Id == ToolTint.ToolId) {
                The.Toolbox.Clear();
            }
            if (UIShowing.Overlay && OverlayId.Tint == UIShowing.OverlayType) {
                A.S.Sig.HideTargetOverlay.Send(OverlayId.Tint);
            }
        }

        private void ClearWidgetRefs() {
            levelRWidget = null;
            levelGWidget = null;
            levelBWidget = null;
            estimateWidget = null;
            colorFooterWidget = null;
            paintDropdown = null;
        }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("Tint");
            int footerIdx = 0;
            int blockIdx = 0;

            // --- Footer 0: current color button → opens color picker ---
            colorFooterWidget = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent4Button, wFooter.Container, footerIdx++);
            UIUtils.AddTip(life, colorFooterWidget, T.SelectedColor);
            colorFooterWidget.Load(IconId.WSquare, null, null, () =>
                ShowColorPickerChoice(colorFooterWidget.Rect));
            RefreshCurrentColor();

            // --- Footer 1: paint mode dropdown ---
            paintDropdown = UISpawn.Widget<WDropdown>(life,
                UIPrefabId.WDropdownWithFrame, wFooter.Container, footerIdx++);
            paintDropdown.Load(paintModeOptions, GetToolModeIdx(), idx => {
                switch (idx) {
                    case 0: sys.BeginDrawIn(WorldLayer.Floor); break;
                    case 1: sys.BeginDrawIn(WorldLayer.Walls); break;
                    case 2: sys.BeginEraseIn(WorldLayer.Floor); break;
                    case 3: sys.BeginEraseIn(WorldLayer.Walls); break;
                }
            });

            // --- Footer 2: eye toggle to show/hide tint layer ---
            var eyeBtn = UISpawn.Widget<WToggleText>(life,
                UIPrefabId.WContent4Toggle, wFooter.Container, footerIdx++);
            eyeBtn.Load(T.Toggle, !sys.IsTintHidden, _ => sys.ToggleTint());
            UIUtils.AddTip(life, eyeBtn, T.OverlayTipTint);
            wFooter.SetActiveFast(true);

            // --- Block 0: Pigment levels ---
            var levelsBlock = UISpawn.Block(life, wContent, blockIdx++);
            int idx2 = 0;

            levelRWidget = UISpawn.Widget<WIconText2Progress>(life,
                UIPrefabId.WContent2Progress, levelsBlock.Container, idx2++);
            levelRWidget.Load(GraphicsId.ObjMaterialsPigmentRed, T.ColorRed, 0f);
            UIUtils.AddTipFunc(life, levelRWidget,
                () => MatType.FastGet(MatIdH.PigmentRed).Def.ExtendedTooltip(S));

            levelGWidget = UISpawn.Widget<WIconText2Progress>(life,
                UIPrefabId.WContent2Progress, levelsBlock.Container, idx2++);
            levelGWidget.Load(GraphicsId.ObjMaterialsPigmentGreen, T.ColorGreen, 0f);
            UIUtils.AddTipFunc(life, levelGWidget,
                () => MatType.FastGet(MatIdH.PigmentGreen).Def.ExtendedTooltip(S));

            levelBWidget = UISpawn.Widget<WIconText2Progress>(life,
                UIPrefabId.WContent2Progress, levelsBlock.Container, idx2++);
            levelBWidget.Load(GraphicsId.ObjMaterialsPigmentBlue, T.ColorBlue, 0f);
            UIUtils.AddTipFunc(life, levelBWidget,
                () => MatType.FastGet(MatIdH.PigmentBlue).Def.ExtendedTooltip(S));

            estimateWidget = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, levelsBlock.Container, idx2++);
            estimateWidget.Load(IconId.CCheck, T.Max, string.Empty);
            UIUtils.AddTip(life, estimateWidget, T.CurrentColorEstimateTip);

            sys.UpdateUIBlock(false);

            // --- Block 1: Saved colors (if any) ---
            if (!Arrays.IsEmpty(sys.SavedColors)) {
                var savedBlock = UISpawn.Block(life, wContent, blockIdx++);
                idx2 = 0;

                var savedHeader = UISpawn.Widget<WText>(life,
                    UIPrefabId.WContent2Header, savedBlock.Container, idx2++);
                savedHeader.Load(T.Saved);

                for (int i = 0; i < sys.SavedColors.Count; i++) {
                    var c = sys.SavedColors[i];
                    var colorRow = UISpawn.Widget<WContentButtonWithX>(life,
                        UIPrefabId.WContent2ButtonX, savedBlock.Container, idx2++);
                    colorRow.Button.Load(IconId.CColorPreview, Colors.ToHex(c, '#'),
                        () => sys.PickToolColor(c));
                    colorRow.Button.SetTint(c);
                    colorRow.XButton.Load(() => {
                        sys.RemoveSavedColor(c);
                        Refresh();
                    });
                }
            }

            return T.OverlayTipTint;
        }

        // Called from TintSys.UpdateUIBlock when pigment levels change
        public void UpdateLevels(float r, float g, float b, float max) {
            if (levelRWidget == null) return;
            float rPct = max > 0 ? r / max : 0f;
            float gPct = max > 0 ? g / max : 0f;
            float bPct = max > 0 ? b / max : 0f;
            levelRWidget.SetProgress(rPct, $"{r:0.##}/{max:0.##}");
            levelGWidget.SetProgress(gPct, $"{g:0.##}/{max:0.##}");
            levelBWidget.SetProgress(bPct, $"{b:0.##}/{max:0.##}");

            if (estimateWidget == null) return;
            if (sys.CurrentToolColor.a > 0) {
                var est = sys.EstimateRemainingTiles(sys.CurrentToolColor, false);
                var estTxt = est < 10
                    ? Texts.WithColor(Units.Num(est), Texts.TMPColorRed)
                    : Units.Num(est);
                estimateWidget.Load(est == 0 ? IconId.CWarning : IconId.CCheck, T.Max, estTxt);
            } else {
                estimateWidget.Load(IconId.CCheck, T.Max, string.Empty);
            }
        }

        // Called from TintSys.AfterChangeConfig when the active color changes
        public void UpdateCurrentColor() {
            RefreshCurrentColor();
        }

        // Called from TintSys.UpdateToolMode when active tool/layer/mode changes
        public void RefreshToolModeIcons() {
            if (paintDropdown != null) paintDropdown.SetValue(GetToolModeIdx(), false);
        }

        private int GetToolModeIdx() {
            if (The.Toolbox.ActiveTool == sys.TintTool) {
                if (sys.CurrentToolMode == "draw" && sys.CurrentToolLayer == WorldLayer.Floor) return 0;
                if (sys.CurrentToolMode == "draw" && sys.CurrentToolLayer == WorldLayer.Walls) return 1;
                if (sys.CurrentToolMode == "erase" && sys.CurrentToolLayer == WorldLayer.Floor) return 2;
                if (sys.CurrentToolMode == "erase" && sys.CurrentToolLayer == WorldLayer.Walls) return 3;
            }
            return 0;
        }

        private void RefreshCurrentColor() {
            if (colorFooterWidget == null) return;
            var color = sys.CurrentToolColor;
            if (color.a == 0) {
                colorFooterWidget.SetImage(IconId.CCross);
                colorFooterWidget.SetTint(Consts.ColorWhite);
                colorFooterWidget.SetText(T.NoColorSelected);
            } else {
                colorFooterWidget.SetImage(IconId.CColorPreview);
                colorFooterWidget.SetTint(new Color(color.r, color.g, color.b, 1f));
                colorFooterWidget.SetText(Colors.ToHex(color, '#'));
            }
        }

        private void ShowColorPickerChoice(RectTransform anchor) {
            var color = sys.CurrentToolColor;
            float a = color.a;

            // Bootstrap a default color if nothing is selected
            if (a == 0) {
                if (color.r == 0 && color.g == 0 && color.b == 0) {
                    color = S.Rng.Color(50, 50);
                }
                color.a = a = 0.5f;
                sys.CurrentToolColor = color;
                sys.AfterColorChanged();
            }

            Color.RGBToHSV(color, out float h, out float s, out float l);
            var colorHex = Colors.ToHex(color);

            // collectSpawnedWidgets=true so we can cast them back to update icon tints
            // SpawnedWidgets layout: [0]=label, [1]=hue, [2]=sat, [3]=lum, [4]=alpha
            var qc = QuickChoice.Create(T.ChangeColor, true);
            qc.Widget = anchor;

            qc.AddSlider(T.ColorHue, new QuickChoice.Slider() {
                Value = h * 360f,
                Min = 0f,
                Max = 360f,
                Icon = Images.Sprite(IconId.WSquare),
                IconTint = Color.HSVToRGB(h, 1f, 1f),
                ValueToText = v => $"{Mathf.RoundToInt(v)}°",
                OnValueChanged = v => {
                    h = v / 360f;
                    ApplyHSVA(h, s, l, a);
                    UpdateColorPickerTints(qc, h, s, l, a);
                },
            });

            qc.AddSlider(T.ColorSaturation, new QuickChoice.Slider() {
                Value = s * 100f,
                Min = 0f,
                Max = 100f,
                Icon = Images.Sprite(IconId.WSquare),
                IconTint = Color.HSVToRGB(h, s, 1f),
                ValueToText = v => $"{Mathf.RoundToInt(v)} %",
                OnValueChanged = v => {
                    s = v / 100f;
                    ApplyHSVA(h, s, l, a);
                    UpdateColorPickerTints(qc, h, s, l, a);
                },
            });

            qc.AddSlider(T.ColorLuminosity, new QuickChoice.Slider() {
                Value = l * 100f,
                Min = 0f,
                Max = 100f,
                Icon = Images.Sprite(IconId.WSquare),
                IconTint = Color.HSVToRGB(0f, 0f, l),
                ValueToText = v => $"{Mathf.RoundToInt(v)} %",
                OnValueChanged = v => {
                    l = v / 100f;
                    ApplyHSVA(h, s, l, a);
                    UpdateColorPickerTints(qc, h, s, l, a);
                },
            });

            qc.AddSlider(T.ColorAlpha, new QuickChoice.Slider() {
                Value = a * 100f,
                Min = 1f,
                Max = 100f,
                Icon = Images.Sprite(IconId.CLightbulb),
                IconTint = new Color(1f, 1f, 1f, Mathf.Max(0.001f, a)),
                ValueToText = v => $"{Mathf.RoundToInt(v)} %",
                OnValueChanged = v => {
                    a = v / 100f;
                    ApplyHSVA(h, s, l, a);
                    UpdateColorPickerTints(qc, h, s, l, a);
                },
            });

            qc.AddButton(T.Confirm, new QuickChoice.Button() {
                OnClick = () => {
                    UpdateCurrentColor();
                },
                AutoClose = true,
            });

            qc.AddButton(T.NounColorPicker, new QuickChoice.Button() {
                OnClick = () => {
                    sys.SwitchToEyeDropper();
                    UpdateCurrentColor();
                },
                Tip = T.ColorPickerTip,
                AutoClose = true,
            });

            qc.AddButton(T.CopyColorHex_V1.F(colorHex), new QuickChoice.Button() {
                OnClick = () => {
                    GUIUtility.systemCopyBuffer = Colors.ToHex(sys.CurrentToolColor);
                    UISounds.PlayShowDialog();
                },
                AutoClose = true,
            });

            var clipboardHex = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrWhiteSpace(clipboardHex)
                    && clipboardHex != colorHex
                    && Colors.TryFromHex(clipboardHex, out var pasteColor, false)) {
                qc.AddButton(T.PasteColorHex_V1.F(clipboardHex), new QuickChoice.Button() {
                    OnClick = () => {
                        sys.CurrentToolColor = pasteColor;
                        sys.AfterColorChanged();
                        UpdateCurrentColor();
                    },
                    AutoClose = true,
                });
            }

            qc.AddButton(T.SaveColor, new QuickChoice.Button() {
                OnClick = () => {
                    sys.SaveCurrentColor();
                    Refresh();
                },
                AutoClose = false,
            });

            qc.Show();
        }

        private void ApplyHSVA(float h, float s, float l, float a) {
            var newColor = Color.HSVToRGB(h, s, l);
            newColor.a = a;
            sys.CurrentToolColor = newColor;
            sys.CurrentToolMode = "draw";
            sys.AfterColorChanged();
            RefreshCurrentColor();
        }

        private static void UpdateColorPickerTints(QuickChoice qc, float h, float s, float l, float a) {
            var ws = qc.SpawnedWidgets;
            if (ws == null || ws.Count < 5) return;
            if (ws[1] is WSliderWithIconText2 hueW)
                hueW.Item.SetTint(Color.HSVToRGB(h, 1f, 1f));
            if (ws[2] is WSliderWithIconText2 satW)
                satW.Item.SetTint(Color.HSVToRGB(h, s, 1f));
            if (ws[3] is WSliderWithIconText2 lumW)
                lumW.Item.SetTint(Color.HSVToRGB(0f, 0f, l));
            if (ws[4] is WSliderWithIconText2 alphaW)
                alphaW.Item.SetTint(new Color(1f, 1f, 1f, Mathf.Max(0.001f, a)));
        }
    }
}
