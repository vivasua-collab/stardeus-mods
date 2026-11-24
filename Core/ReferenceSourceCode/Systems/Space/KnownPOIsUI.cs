using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    /// <summary>
    /// POI = point of interest
    /// </summary>
    public sealed class KnownPOIsUI {
        readonly GameState S;
        readonly StarmapContext ctx;
        readonly StarmapMenuWidget parent;
        private bool contentsExpanded = true;
        private Lifetime contentsLifetime;
        Dictionary<MatType, Dictionary<SpaceObject, int>> matToSOs = new();
        Dictionary<SpaceObjectType, Dictionary<SpaceObject, int>> poiToSOs = new();
        List<Ships.NPCShip> allShips = new();

        // number of undiscovered-vs-discovered deposits of mat in so's biome that
        // are actually known - mirrors SOInfo.LoadKnownRegionMats' counting
        private static int KnownDepositCount(SpaceObject so, MatType mat) {
            if (Arrays.IsEmpty(so.Mats)) { return 1; }
            int count = 0;
            foreach (var m in so.Mats) {
                if (m == mat) { count++; }
            }
            return count > 0 ? count : 1;
        }
        public KnownPOIsUI(GameState s, StarmapContext ctx, StarmapMenuWidget parent) {
            S = s;
            this.ctx = ctx;
            this.parent = parent;
        }

        private void AddUIDetails_Universe(SpaceObject so, RectTransform tr) {
            int idx = 0;
            matToSOs.Clear();
            poiToSOs.Clear();
            allShips.Clear();

            foreach (var reg in so.Children.OrderBy(c => c.DisplayName)) {
                foreach (var child in reg.Children.OrderBy(c => c.DisplayName)) {
                    if (!child.HasKnowledge) { continue; }
                    if (!Arrays.IsEmpty(child.Knowledge.Mats)) {
                        foreach (var mat in child.Knowledge.Mats) {
                            var mt = MatType.FastGet(mat);
                            if (mt == null) { continue; }
                            matToSOs.GetInit(mt, () => new Dictionary<SpaceObject, int>())
                                .GetIncrement(reg, KnownDepositCount(child, mt));
                        }
                    }
                    if (!Arrays.IsEmpty(child.Knowledge.POIs)) {
                        foreach (var poi in child.Knowledge.POIs) {
                            if (poi.Value <= 0) { continue; }
                            var type = SpaceObjectType.Find(poi.Key);
                            if (type == null) { continue; }
                            poiToSOs.GetInit(type, () => new Dictionary<SpaceObject, int>())
                                .GetIncrement(reg, poi.Value);
                        }
                    }
                }
            }
            foreach (var mat in matToSOs.OrderBy(kv => kv.Key.NameT)) {
                AddGroup(mat.Key.IconId, mat.Key.NameT, mat.Key.Def, null, mat.Value, tr, idx++);
            }
            foreach (var poi in poiToSOs.OrderBy(kv => kv.Key.NameT)) {
                AddGroup(poi.Key.Icon, poi.Key.NameT, null, poi.Key.InfoT, poi.Value, tr, idx++);
            }
        }

        private void AddGroup(string icon, string name, Def codexDef, string fallbackTip,
                Dictionary<SpaceObject, int> sos, RectTransform tr, int idx) {
            int total = 0;
            foreach (var kv in sos) { total += kv.Value; }
            var num = total > 1 ? Units.XNum(total) : null;
            var w = UISpawn.Widget<WContentListButton>(contentsLifetime, UIPrefabId.WContent2ListButton, tr, idx);
            w.Load(icon, name, num);
            if (codexDef != null) {
                UIUtils.AddCodexTooltip(contentsLifetime, w.PopupArea.gameObject, w, codexDef);
            } else if (fallbackTip != null) {
                UIUtils.AddTipFunc(contentsLifetime, w, () => fallbackTip);
            }
            w.LoadOnToggle(contentsLifetime, (life, container, willShow, wasLoaded) => {
                if (!willShow) {
                    return wasLoaded;
                }
                if (wasLoaded) {
                    return true;
                }
                int subIdx = 0;
                foreach (var so2 in sos.OrderBy(s => s.Key.DisplayName)) {
                    var w2 = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, subIdx++);
                    var subNum = so2.Value > 1 ? Units.XNum(so2.Value) : null;
                    w2.Load(icon, so2.Key.DisplayName, subNum, () => {
                        S.Sys.Universe.ShowInStarmap(so2.Key);
                    });
                    UIUtils.AddTipFunc(life, w2, () => {
                        S.UIQuery.HideCodexTooltip.Ask();
                        return so2.Key.Info.UITooltip(true);
                    });
                }
                return true;
            });
        }

        private void AddUIDetails_Sector(SpaceObject so, RectTransform tr) {
            matToSOs.Clear();
            allShips.Clear();
            int idx = 0;
            foreach (var child in so.Children.OrderBy(c => c.DisplayName)) {
                if (child.Ship != null) { continue; }
                var ch = child;
                if (child.Type.InheritParentLooks) {
                    ch = so;
                }
                var w = UISpawn.Widget<WButtonIconText2>(contentsLifetime, UIPrefabId.WContent2Button, tr, idx++);
                w.Load(ch.Icon, ch.DisplayName, null, () => {
                    S.Sys.Universe.ShowInStarmap(ch);
                    SpaceObjectUICtx.ShowQuickChoiceFor(ch, ch.UI);
                });
                UIUtils.AddTipFunc(contentsLifetime, w, () => ch.Info.UITooltip(true));
            }
            foreach (var child in so.Children.OrderBy(c => c.DisplayName)) {
                if (child.Ship == null) { continue; }
                var ship = child.Ship;
                AddShipWidget(tr, ship, idx++);
            }
        }

        private void AddShipWidget(RectTransform tr, Ships.NPCShip ship, int idx) {
            var icon = ship.IsPlayer ? IconId.CComputing : ship.Class.Preview;
            var w = UISpawn.Widget<WButtonIconText2>(contentsLifetime, UIPrefabId.WContent2Button, tr, idx);
            var capSO = ship.SO;
            w.Load(icon, ship.NameWithTag, null, () => {
                if (ctx.Layer == UniverseLayer.Sector) {
                    S.Sys.Universe.ShowInStarmap(capSO);
                    SpaceObjectUICtx.ShowQuickChoiceFor(capSO, capSO.UI);
                } else {
                    S.Sys.Universe.ShowInStarmap(ship.Sector.SO);
                }
            });
            UIUtils.AddTipFunc(contentsLifetime, w, () => ship.FullTooltip(true));
        }

        private void AddUIDetails_Region(SpaceObject so, RectTransform transform) {
            int idx = 0;
            matToSOs.Clear();
            poiToSOs.Clear();
            allShips.Clear();

            foreach (var child in so.Children.OrderBy(c => c.DisplayName)) {
                if (!child.HasKnowledge) { continue; }
                if (!Arrays.IsEmpty(child.Knowledge.Mats)) {
                    foreach (var mat in child.Knowledge.Mats) {
                        var mt = MatType.FastGet(mat);
                        if (mt == null) { continue; }
                        matToSOs.GetInit(mt, () => new Dictionary<SpaceObject, int>())
                            [child] = KnownDepositCount(child, mt);
                    }
                }
                if (!Arrays.IsEmpty(child.Knowledge.POIs)) {
                    foreach (var poi in child.Knowledge.POIs) {
                        if (poi.Value <= 0) { continue; }
                        var type = SpaceObjectType.Find(poi.Key);
                        if (type == null) { continue; }
                        poiToSOs.GetInit(type, () => new Dictionary<SpaceObject, int>())
                            [child] = poi.Value;
                    }
                }
            }

            foreach (var sec in so.Region.Sectors) {
                var ships = S.Query.NPCShipsAt.Ask(sec);
                if (!Arrays.IsEmpty(ships)) {
                    allShips.AddRange(ships);
                }
            }

            foreach (var mat in matToSOs.OrderBy(kv => kv.Key.NameT)) {
                AddGroup(mat.Key.IconId, mat.Key.NameT, mat.Key.Def, null, mat.Value, transform, idx++);
            }
            foreach (var poi in poiToSOs.OrderBy(kv => kv.Key.NameT)) {
                AddGroup(poi.Key.Icon, poi.Key.NameT, null, poi.Key.InfoT, poi.Value, transform, idx++);
            }

            if (allShips.Count > 0) {
                AddShips(allShips, transform, idx++);
            }
        }

        private void AddShips(List<Ships.NPCShip> ships, RectTransform tr, int idx) {
            var num = ships.Count > 1 ? Units.XNum(ships.Count) : null;
            var w = UISpawn.Widget<WContentListButton>(contentsLifetime, UIPrefabId.WContent2ListButton, tr, idx);
            var icon = IconId.CSpaceShip;
            w.Load(icon, T.NounShips, num);
            w.LoadOnToggle(contentsLifetime, (life, container, willShow, wasLoaded) => {
                if (!willShow) {
                    return wasLoaded;
                }
                if (wasLoaded) {
                    return true;
                }
                int subIdx = 0;
                foreach (var ship in ships.OrderBy(s => s.NameWithTag)) {
                    AddShipWidget(container, ship, subIdx++);
                }
                return true;
            });
        }

        public void AddMainButton(WContentBlock block, int idx) {
            if (!S.Sys.Sec.IsRadarActive()) { return; }

            var mainLifetime = ctx.MenuLifetime;
            var w = UISpawn.Widget<WContentListButton>(mainLifetime, UIPrefabId.WContent2ListButton, block.Container, idx);
            var name = SOUtils.FullName(A.PlayerParentInStarmap, false);
            w.Load(IconId.CStarmap, name, null);
            w.LoadOnToggle(contentsLifetime, (life, tr, sh, l) =>
                ToggleContents(w, tr, sh, l), contentsExpanded);
        }

        private bool ToggleContents(WContentListButton w, RectTransform tr,
                bool willShow, bool wasLoaded) {
            contentsExpanded = willShow;
            if (!willShow) {
                if (contentsLifetime != null) {
                    contentsLifetime.Dispose();
                    contentsLifetime = null;
                }
                return false;
            }
            if (contentsLifetime != null) {
                contentsLifetime.Dispose();
            }
            contentsLifetime = ctx.MenuLifetime.CreateChild("KnownPOIs");
            switch (ctx.Layer) {
                case UniverseLayer.Universe:
                    AddUIDetails_Universe(ctx.ParentSO, tr);
                    break;
                case UniverseLayer.Region:
                    AddUIDetails_Region(ctx.ParentSO, tr);
                    break;
                case UniverseLayer.Sector:
                    AddUIDetails_Sector(ctx.ParentSO, tr);
                    break;
                default:
                    D.Throw("Unknown starmap layer when adding known POIs: {0}", ctx.Layer);
                    break;
            }
            return true;
        }
    }
}
