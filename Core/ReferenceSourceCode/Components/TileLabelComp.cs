using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Text;
using KL.Utils;
using UnityEngine;

namespace Game.Components {
    public sealed class TileLabelComp : BaseComponent<TileLabelComp>, IRelocatable {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TileLabelComp());
        }

        internal float offsetY;
        internal float width;
        internal float size;
        private GameTextTMP labelText;
        public string Label;
        internal string note;
        public Color Color;
        internal string colorId;
        private bool requireResearch;
        public bool IsRoomName;
        internal bool isAlwaysDisplayed;

        public void Hide() {
            if (isShowingInfo) {
                isShowingInfo = false;
                HideInfoIcon();
            }
            HideLabel();
        }

        public void Show() {
            SetLabel(Label);
        }

        public string DecoratedName(string nameT) {
            if (Label == null || nameT == Label) {
                return nameT;
            } else {
                return $"{nameT} {Texts.GreySmaller(Label)}";
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Label != null) {
                if (Tile.IsConstructed) {
                    SetLabel(Label);
                    if (!isAlwaysDisplayed) {
                        SubscribeToTilesSelected();
                    }
                }
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                if (Label != null) {
                    SetLabel(Label);
                    if (isAlwaysDisplayed) {
                        ShowLabel(true);
                    } else {
                        SubscribeToTilesSelected();
                    }
                }
            }
        }

        private bool isShowingInfo;
        private bool isShowingLabel;
        private bool isSubscribedToTilesSelected;

        public void ShowLabel(bool textChanged = false) {
            if (isShowingLabel && !textChanged) { return; }
            if (labelText == null) {
                var pos = Tile.Position + new Vector2(0f, offsetY);
                labelText = GameTextTMP.Create("TileLabel", Label, pos);
            } else {
                labelText.SetText(Label);
            }
            labelText.SetFontSize(size);
            labelText.SetColor(Color);
            isShowingLabel = true;
        }
        public void HideLabel() {
            if (!isShowingLabel) { return; }
            labelText?.Destroy();
            labelText = null;
            isShowingLabel = false;
        }

        public void SetLabel(string l) {
            Label = l;
            if (string.IsNullOrWhiteSpace(l)) {
                if (isShowingInfo) {
                    isShowingInfo = false;
                    HideInfoText();
                }
                return;
            }
            if (isAlwaysDisplayed) {
                ShowLabel(true);
            }
            ShowLabelInfo();
            isShowingInfo = true;
            UpdateWidth();
        }

        public void SetNote(string n) {
            note = n;
            ShowLabelInfo();
        }

        public void ShowLabelInfo() {
            var icon = isAlwaysDisplayed ? IconId.CTransparent : IconId.CLabelSmall;
            if (Label != null && note != null) {
                ShowInfoIcon(icon, $"{Label}{GlyphMap.BRHR}{note}");
            } else if (Label == null && note != null) {
                ShowInfoIcon(icon, note);
            } else if (Label != null && note == null) {
                ShowInfoIcon(icon, Label);
            } else {
                HideInfoIcon();
            }
        }

        public void SetColor(Color color) {
            this.Color = color;
            labelText?.SetColor(color);
        }

        protected override void OnConfig() {
            requireResearch = Config.GetBool(PropertyIdH.RequireResearch, true);
            Label = Config.GetString(PropertyIdH.Label, null);
            offsetY = Config.GetFloat(PropertyIdH.OffsetY, -0.5f);
            size = Config.GetFloat(PropertyIdH.Size, 2f);
            width = Config.GetFloat(PropertyIdH.Width, 4f);
            colorId = Config.GetString(PropertyIdH.ColorID, "Yellow");
            Color = Palette.ColorById(colorId);
            IsRoomName = true;
            isAlwaysDisplayed = true;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetString("Label", Label);
            data.SetString("Note", note);
            if (Label != null) {
                data.SetFloat("OffY", offsetY);
                data.SetFloat("Size", size);
                data.SetString("ColorId", colorId);
                data.SetFloat("Width", width);
                data.SetBool("IsRoomName", IsRoomName);
                data.SetBool("IsAlwaysDisplayed", isAlwaysDisplayed);
            }
        }

        protected override void OnLoad(ComponentData data) {
            Label = data.GetString("Label", null);
            note = data.GetString("Note", null);
            if (Label != null) {
                offsetY = data.GetFloat("OffY", offsetY);
                size = data.GetFloat("Size", size);
                width = data.GetFloat("Width", width);
                colorId = data.GetString("ColorId", colorId);
                Color = Palette.ColorById(colorId);
                IsRoomName = data.GetBool("IsRoomName", IsRoomName);
                isAlwaysDisplayed = data.GetBool("IsAlwaysDisplayed", isAlwaysDisplayed);
            }
        }

        public override void OnRemove() {
            UnsubscribeFromTilesSelected();
            // if we remove the label here, relocation will not work
            // Label = null;
            Hide();
            S.Sig.LabelChanged.Send(this);
        }


        public void UpdatePosition() {
            if (labelText != null) {
                var tileP = Tile.Position;
                var pos = new Vector3(tileP.x, tileP.y + offsetY, 0);
                labelText.MoveTo(pos);
            }
        }

        public void UpdateSize() {
            labelText?.SetFontSize(size);
        }

        public void UpdateWidth() {
            if (labelText?.Transform is RectTransform rt) {
                rt.sizeDelta = new Vector2(width, 1f);
            }
        }

        public override string ToString() {
            return string.Format(" * TileLabel [{0}]", Label);
        }

        public bool CanConfigure => Tile.IsConstructed &&
            (!requireResearch || S.Research.IsUnlocked(
                DefIdsH.ResearchHabitationLabeling));


        private void UnsubscribeFromTilesSelected() {
            if (isSubscribedToTilesSelected) {
                isSubscribedToTilesSelected = false;
                S.Sig.TilesSelected.RemoveListener(OnTilesSelected);
                S.Sig.MultiSelect.RemoveListener(OnMultiSelect);
            }
        }

        private void OnMultiSelect(List<Being> beings, List<Obj> objs, List<Tile> tiles) {
            OnTilesSelected(tiles);
        }

        private void SubscribeToTilesSelected() {
            if (!isSubscribedToTilesSelected) {
                isSubscribedToTilesSelected = true;
                S.Sig.TilesSelected.AddListener(OnTilesSelected);
                S.Sig.MultiSelect.AddListener(OnMultiSelect);
            }
        }

        private void OnTilesSelected(List<Tile> tiles) {
            if (Arrays.IsEmpty(tiles)) {
                HideLabel();
                return;
            }
            if (Tile.Transform.IsMulti) {
                for (int i = tiles.Count - 1; i >= 0; i--) {
                    Tile t = tiles[i];
                    if (t.Transform.IsChild) {
                        t = t.Transform.Parent;
                    }
                    if (t == Tile) {
                        ShowLabel();
                        return;
                    }
                }
                HideLabel();
            } else {
                if (tiles.Contains(Tile)) {
                    ShowLabel();
                } else {
                    HideLabel();
                }
            }
        }

        internal void ToggleAlwaysShow() {
            isAlwaysDisplayed = !isAlwaysDisplayed;
            if (isAlwaysDisplayed) {
                UnsubscribeFromTilesSelected();
                ShowLabel();
            } else {
                HideLabel();
                SubscribeToTilesSelected();
            }
            ShowLabelInfo();
        }

        internal void ToggleRoomName() {
            IsRoomName = !IsRoomName;
            if (UIShowing.Overlay && UIShowing.OverlayType == OverlayId.Areas) {
                S.Sig.HideOverlay.Send();
                S.Sig.ShowOverlay.Send(OverlayId.Areas);
            }
            S.Sig.LabelChanged.Send(this);
        }

        internal void AfterRename(string newLabel) {
            if (string.IsNullOrWhiteSpace(newLabel)) {
                Label = null;
                Hide();
                UnsubscribeFromTilesSelected();
            } else {
                SetLabel(newLabel);
                if (!isAlwaysDisplayed) {
                    SubscribeToTilesSelected();
                    ShowLabel(true);
                }
            }
            S.Sig.LabelChanged.Send(this);
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (Label == null || labelText == null) { return; }
            labelText.MoveTo(EntityUtils.CenterOf(pos) + new Vector2(0, offsetY));
        }

        internal void OnChangeColor(ColorDef col) {
            colorId = col.Id;
            SetColor(col.Color);
            S.Sig.LabelChanged.Send(this);
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<TileLabelComp>(out var lc)) {
                lc.offsetY = offsetY;
                lc.Color = Color;
                lc.colorId = colorId;
                lc.size = size;
                lc.width = width;
                lc.IsRoomName = IsRoomName;
                lc.isAlwaysDisplayed = isAlwaysDisplayed;
                if (!isAlwaysDisplayed) {
                    lc.SubscribeToTilesSelected();
                }
                lc.SetLabel(Label);
                S.Sig.LabelChanged.Send(this);
            }
        }
    }
}
