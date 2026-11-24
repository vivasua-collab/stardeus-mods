#if UNITY_EDITOR
// #define DEBUG_OUTSIDE
#endif
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Systems.Sleep;
using Game.Systems.UIUX;
using Game.Utils;
using Game.Visuals.Hull;
using KL.Utils;
using KL.Grid;
using Game.Systems.Creatures;

namespace Game.Systems.Areas {
    public sealed class AreasContextPopup : IOverlayLegendPopup {
        private static readonly int popupHash = Hashes.S("AreasOverlay");
        public int UniquePopupHash => popupHash;
        public bool ShowInScreenCenter => true;

        private readonly GameState S;
        private readonly AreaSys sys;
        private bool isOpen;

        private readonly Dictionary<Def, List<Tile>> roomDevices = new();

        public AreasContextPopup(GameState s, AreaSys sys) {
            S = s;
            this.sys = sys;
        }

        public void Show() => S.UIQuery.ShowContextPopup.Ask(this, true);
        public void Hide() => S.UIQuery.ShowContextPopup.Ask(this, false);

        public void Refresh() {
            if (isOpen) {
                Show();
            }
        }

        public void OnPopupOpened() {
            isOpen = true;
        }

        public void OnPopupClosed() {
            isOpen = false;
            if (!sys.IsSwitchingOverlay) {
                sys.SetHighlightedAreaId(0);
                sys.SetSpaceHighlighted(false);
                A.S.Sig.HideTargetOverlay.Send(OverlayId.Areas);
            }
        }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("Areas");
            var block = UISpawn.Block(life, wContent, 0);
            int idx = 0;

            sys.SetHighlightedAreaId(0);
            sys.SetSpaceHighlighted(false);

            var areaLabels = BuildAreaLabels();

            // Roof toggle
            var roofBtn = UISpawn.Widget<WToggleText>(life,
                UIPrefabId.WContent4Toggle, wFooter.Container, idx++);
            roofBtn.Load(T.ShowRoof, sys.IsRoofMode, (v) => {
                sys.SetRoofMode(v);;
            });
            UIUtils.AddTip(life, roofBtn, T.ShowRoofTip);

            // Hull style: every tileset by its folder name, so mod hulls show up by themselves
            var ship = S.Universe.Player;
            var tilesets = HullDetails.Tilesets();
            // a style whose mod is gone shows as Default, which is what is drawn
            int current = tilesets.IndexOf(HullStyles.TilesetOf(ship));
            if (current < 0) {
                current = 0;
            }
            var styleDropdown = UISpawn.Widget<WDropdown>(life,
                UIPrefabId.WDropdownWithFrame, wFooter.Container, idx++);
            styleDropdown.Load(tilesets, current,
                i => {
                    HullStyles.SetTileset(ship, tilesets[i]);
                    // so the new look shows right away
                    if (!sys.IsRoofMode) {
                        roofBtn.SetValue(true);
                    }
                });
            UIUtils.AddTip(life, styleDropdown, T.NounShipHull);

            wFooter.SetActiveFast(true);

            // Landing zone indicator
            var lzRow = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            if (sys.LandableAreaSize < Consts.MinLandingZoneSize) {
                lzRow.Load(IconId.CWarning, T.NoLandingZone, Units.Num(sys.LandableAreaSize));
            } else {
                lzRow.Load(IconId.CStripeZone, T.AreaExposedToSpace,
                    Units.Num(sys.LandableAreaSize));
                lzRow.SetTint(Consts.ColorLandingZone);
            }
            var lzTip = UIUtils.AddTipFunc(life, lzRow, sys.GetLandingZoneTip);
            lzTip.HoverFun = sys.SetSpaceHighlighted;

            // Area list, rooms with a problem first
            foreach (var area in sys.Areas
                    .OrderByDescending(a => a.Value.Count)
                    .OrderByDescending(a => sys.DoorCheck.IsUnreachable(a.Key)
                        || sys.CornerGaps.CountIn(a.Key) > 0)) {
                var areaId = area.Key;
                var capturedArea = area;
                var roomText = BuildRoomText(areaId, area.Value, areaLabels);
                var iconTint = sys.ColorForArea(areaId);

                var areaBtn = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                areaBtn.Load(IconId.CRoom, roomText, Units.Num(area.Value.Count), () => {
                    S.Sig.CameraFocusTiles.Send(capturedArea.Value);
                });
                areaBtn.SetTint(iconTint);
                var tip = UIUtils.AddTipFunc(life, areaBtn, () => BuildAreaTooltip(areaId));
                tip.HoverFun = on => {
                    if (on) {
                        S.Sig.CameraShowTilesLink.Send(null, capturedArea.Value,
                            CameraFocus.FlashColor());
                        sys.SetHighlightedAreaId(capturedArea.Key);
                    } else {
                        S.Query.CameraHideLink.Ask();
                        sys.SetHighlightedAreaId(0);
                    }
                };
            }

            #if DEBUG_OUTSIDE
            foreach (var area in sys.OuterAreas.OrderByDescending(a => a.Value.Count)) {
                var capturedArea = area;
                var outerBtn = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                outerBtn.Load(IconId.CRoom, $"{T.OuterRoomId_V1.F(-area.Key)}",
                    Units.Num(area.Value.Count), () => {
                        S.Sig.CameraFocusTiles.Send(capturedArea.Value);
                    });
            }
            #endif

            return $"{T.EnclosedAreas} ({sys.Count})";
        }

        private Dictionary<int, TileLabelComp> BuildAreaLabels() {
            var result = new Dictionary<int, TileLabelComp>();
            int found = S.Components.FindAllMatching(
                Caches.TileLabels, (tl) => !string.IsNullOrWhiteSpace(tl.Label));
            for (int i = 0; i < found; i++) {
                var labelObj = Caches.TileLabels[i];
                var roomId = sys.At(labelObj.Tile.PosIdx);
                if (labelObj.IsRoomName && !result.ContainsKey(roomId)) {
                    result.Add(roomId, labelObj);
                }
            }
            return result;
        }

        // The rule itself is AreaSys.PersonalQuartersAreaOf, so the label never disagrees with
        // the personal quarters quest; this only finds whose bed to ask about. The size test
        // repeats part of the rule only to skip walking rooms that can never qualify.
        private BeingData FindPersonalQuartersOwner(int areaId, HashSet<int> areaTiles) {
            var size = areaTiles.Count;
            if (size < Tunables.Room_PersonalQuartersMinSize) return null;
            if (size > Tunables.Room_PersonalQuartersMaxSize) return null;
            foreach (var posIdx in areaTiles) {
                var obj = EntityUtils.ObjAt(posIdx);
                if (obj == null) continue;
                if (!obj.TryGetComponent<BedComp>(out var bed)) continue;
                // a bed blueprint: slots are made when it is built
                if (bed.Slots == null) {
                    continue;
                }
                foreach (var slot in bed.Slots) {
                    if (slot.IsTemporary) continue;
                    if (slot.OwnerDataId == 0) continue;
                    var owner = slot.Owner;
                    if (owner != null && sys.PersonalQuartersAreaOf(owner) == areaId) {
                        return owner;
                    }
                }
            }
            return null;
        }

        private string BuildRoomText(int areaId, HashSet<int> areaTiles,
                Dictionary<int, TileLabelComp> areaLabels) {
            string roomText = null;
            if (areaLabels.TryGetValue(areaId, out var labelObj)) {
                roomText = Texts.Accented(labelObj.Label);
            } else {
                var owner = FindPersonalQuartersOwner(areaId, areaTiles);
                if (owner != null) {
                    roomText = T.PersonalRoom_V1.F(owner.Mind.Name.ShortName);
                }
            }
            if (sys.DoorCheck.IsUnreachable(areaId)) {
                if (roomText != null) {
                    roomText = string.Format("{0}\n</size><size=70%>{1}</size>",
                        T.RoomNrUnreachable_V2.F(areaId, Texts.TMPColorRed), roomText);
                } else {
                    roomText = T.RoomNrUnreachable_V2.F(areaId, Texts.TMPColorRed);
                }
            } else {
                var roomIdTxt = T.RoomId_V1.F(areaId);
                if (roomText == null) {
                    roomText = roomIdTxt;
                }
            }
            // A line of its own, so it shows next to UNREACHABLE too (a fresh room is often both)
            var gaps = sys.CornerGaps.CountIn(areaId);
            if (gaps > 0) {
                var gapsText = Texts.WithColor(Texts.Bold(T.RoomCornerGaps_V1.F(gaps)),
                    Texts.TMPColorRed);
                roomText = $"{roomText}\n<size=70%>{gapsText}</size>";
            }
            return roomText;
        }

        private string BuildAreaTooltip(int areaId) {
            Caches.StringBuilder.Clear();
            if (sys.CornerGaps.CountIn(areaId) > 0) {
                Caches.StringBuilder.Append(T.RoomCornerGapsTip);
                Caches.StringBuilder.Append("\n\n");
            }
            int found = S.Components.FindAllMatching(
                Caches.TileLabels, (tl) => !string.IsNullOrWhiteSpace(tl.Label));
            bool hasLabel = false;
            for (int i = 0; i < found; i++) {
                var labelObj = Caches.TileLabels[i];
                if (sys.At(labelObj.Tile.PosIdx) == areaId) {
                    Caches.StringBuilder.AppendFormat("- {0}\n", Texts.Accented(labelObj.Label));
                    hasLabel = true;
                }
            }
            roomDevices.Clear();
            if (!sys.Areas.TryGetValue(areaId, out var area)) {
                return hasLabel ? Caches.StringBuilder.ToString() : null;
            }
            foreach (var p in area) {
                var obj = EntityUtils.ObjAt(p);
                if (obj != null && !obj.Transform.IsChild) {
                    if (!roomDevices.TryGetValue(obj.Definition, out var list)) {
                        list = new List<Tile>();
                        roomDevices[obj.Definition] = list;
                    }
                    list.Add(obj);
                }
            }
            foreach (var kv in roomDevices.OrderBy(kv => kv.Key.NameT)) {
                if (kv.Value.Count > 1) {
                    Caches.StringBuilder.AppendFormat("- {0} x{1}\n",
                        kv.Value[0].NameLink, kv.Value.Count);
                } else {
                    Caches.StringBuilder.AppendFormat("- {0}\n", kv.Value[0].NameLink);
                }
            }
            roomDevices.Clear();
            return Caches.StringBuilder.ToString();
        }
    }
}
