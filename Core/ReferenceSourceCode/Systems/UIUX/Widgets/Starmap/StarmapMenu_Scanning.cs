using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StarmapMenu_Scanning : StarmapMenuBlock {
        public override int Order => 20;
        private bool expandResources;
        private bool isShowingDeepFocus;
        private Lifetime lifetime;
        private Lifetime subPanelLifetime;

        public StarmapMenu_Scanning(StarmapMenuWidget parent, StarmapContext ctx, GameState s) : base(parent, ctx, s) {
        }

        public override void Load(int siblingIdx) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Scanning");

            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;
            var radarActive = S.Sys.Sec.RadarActiveB;
            bool hasRadar = radarActive.Value;
            radarActive.Bind(
                lifetime, _ => parent.Load(), notifyImmediately: false);
            var header = UISpawn.Widget<WText>(lifetime, hasRadar ?
                UIPrefabId.WContent2Header : UIPrefabId.WContentHeader,
                block.Container, idx++);
            header.Load(T.ResearchScanning);

            if (!hasRadar) {
                var w = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, block.Container, idx);
                w.Load(IconId.CWarning, T.RadarNotAvailable);
                UIUtils.AddCodexTooltip(lifetime,
                    w.gameObject, w, The.Defs.FastGet(DefIdsH.ObjectsDevicesRadar));
                return;
            }
            var sys = S.Sys.Scan;

            AddScan(sys, block, idx++);
            // Sectors that can't hold deposits (trade zones, derelict zones) never deep scan
            if (sys.IsDeepScanAvailable(out _)) {
                AddDeepScan(sys, block, idx++);
            }
        }

        private void AddDeepScan(ScanSys sys, WContentBlock block, int idx) {
            var w0 = UISpawn.Widget<WButtonIconText2Progress>(lifetime,
                UIPrefabId.WContent2ProgressButton, block.Container, idx);
            w0.Load(IconId.CMagnifier, T.ScanTargetDeep, 0f,
                () => ToggleDeepFocusChoice(w0), T.NotAvailable);
            UIUtils.AddTipFunc(lifetime, w0, ShowDeepScanTooltip);

            sys.DeepFocus.Bind(lifetime, (mat) => {
                if (mat == null) {
                    w0.Item.SetImage(IconId.CMagnifier);
                    w0.Item.SetText(T.ScanTargetDeep);
                } else {
                    w0.Item.SetImage(mat.IconId);
                    w0.Item.SetText(T.ScanDeepFocus_V1.F(mat.NameT));
                }
            });

            sys.ScanDeepProgress.Bind(lifetime, (pct) => {
                if (sys.ScanDeepProgress < 0f) {
                    w0.SetPercent(0f, false);
                    w0.Item.SetText2(T.AdjDone);
                } else {
                    w0.SetPercent(pct);
                }
            });
        }

        private string ShowDeepScanTooltip() {
            var sys = S.Sys.Scan;
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append(T.ScanTargetDeepDesc);
            sb.Append(GlyphMap.BRHR);
            sb.Append(T.ScanDeepFocusDesc);
            var focus = sys.DeepFocus.Value;
            if (focus != null) {
                sb.Append(GlyphMap.BRHR);
                if (sys.IsFocusUsedUp(S.Universe.Player.Parent)) {
                    sb.Append(Texts.Yellow(T.ScanDeepFocusUsed));
                } else if (sys.TryGetFocusChance(out var chance)) {
                    sb.Append(Texts.Green(T.ScanDeepFocusChance_V2.F(focus.NameT,
                        Units.PercentageRounded(chance))));
                } else {
                    sb.Append(Texts.Yellow(T.ScanDeepFocusImpossible_V1.F(focus.NameT)));
                }
            }
            return sb.ToString();
        }

        private void ToggleDeepFocusChoice(WBase owner) {
            var p = parent.SubPanel;
            if (subPanelLifetime != null) {
                subPanelLifetime.Dispose();
            }
            subPanelLifetime = lifetime.CreateChild("SubPanel");
            isShowingDeepFocus = true;
            p.SetTitle(T.ScanTargetDeep);

            var sys = S.Sys.Scan;
            var bl = UISpawn.Widget<WContentBlock>(subPanelLifetime,
                UIPrefabId.WContentBlock, p.Container.Rect, 0);
            var bIdx = 0;
            var label = UISpawn.Widget<WText>(subPanelLifetime,
                UIPrefabId.WContent2Header, bl.Container, bIdx++);
            label.Load(T.SelectTarget);

            var btnAny = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                UIPrefabId.WContent2Button, bl.Container, bIdx++);
            btnAny.Load(IconId.CMagnifier, T.PronounAny, null, () => {
                sys.SetDeepFocus(null);
                parent.SubPanel.Close();
            });

            SpawnMatList(subPanelLifetime, bl.Container, bIdx, (type) => {
                sys.SetDeepFocus(type);
                parent.SubPanel.Close();
                S.UIQuery.HideCodexTooltip.Ask();
            }, ScanSys.MaxDeepFocusRarity);

            p.Show(owner);
        }

        private void AddScan(ScanSys sys, WContentBlock block, int idx) {
            var w0 = UISpawn.Widget<WButtonIconText2Progress>(lifetime,
                UIPrefabId.WContent2ProgressButton, block.Container, idx);
            w0.Load(IconId.CCommunications, T.ScanTargetNone, 0f,
                () => ToggleScanChoice(w0, false), string.Empty);
            UIUtils.AddTipFunc(lifetime, w0, ShowScanTooltip);

            var w = w0.Item;

            sys.Mode.Bind(lifetime, (mode) => {
                w.SetText2(string.Empty);
                w0.SetPercent(0f, false);
                switch (mode) {
                    case ScanMode.None:
                        w.SetImage(IconId.CCommunications);
                        w.SetText(T.ScanTarget_V1.F(T.ScanTargetNone));
                        break;
                    case ScanMode.HyperjumpRelay:
                        w.SetImage(IconId.CHyperspaceEnter);
                        w.SetText(T.ScanTarget_V1.F(T.SpaceObjectHyperjumprelay));
                        break;
                    case ScanMode.SpaceStation:
                        w.SetImage(IconId.CTrade);
                        w.SetText(T.ScanTarget_V1.F(T.SpaceObjectSpacestation));
                        break;
                    case ScanMode.Anomaly:
                        w.SetImage(IconId.CWarning);
                        w.SetText(T.ScanTarget_V1.F(T.SpaceObjectAnomaly));
                        break;
                    case ScanMode.DerelictShip:
                        w.SetImage(GraphicsId.SpaceObjectsDerelictsShipFighter01_Derelict_L);
                        w.SetText(T.ScanTarget_V1.F(T.ScanTargetDerelictShip));
                        break;
                    case ScanMode.Mat:
                        // MatTarget binding will handle this
                        break;
                    default:
                        D.Err($"Unhandled scan mode in starmap menu: {mode}");
                        break;
                }
            });

            sys.FoundSO.Bind(lifetime, (so) => {
                if (so == null) { return; }
                if (parent.SubPanel.IsShowing && !isShowingDeepFocus) {
                    ToggleScanChoice(parent.SubPanel.Container, true);
                }
                switch (sys.Mode.Value) {
                    case ScanMode.None:
                        return;
                    case ScanMode.HyperjumpRelay:
                        w.SetImage(IconId.CHyperspaceEnter);
                        w.SetText(T.ScanFound_V1.F(T.SpaceObjectHyperjumprelay));
                        break;
                    case ScanMode.SpaceStation:
                        w.SetImage(IconId.CTrade);
                        w.SetText(T.ScanFound_V1.F(T.SpaceObjectSpacestation));
                        break;
                    case ScanMode.Anomaly:
                        w.SetImage(IconId.CWarning);
                        w.SetText(T.ScanFound_V1.F(T.SpaceObjectAnomaly));
                        break;
                    case ScanMode.DerelictShip:
                        w.SetImage(GraphicsId.SpaceObjectsDerelictsShipFighter01_Derelict_L);
                        w.SetText(T.ScanFound_V1.F(T.ScanTargetDerelictShip));
                        break;
                    case ScanMode.Mat:
                        // MatTarget binding will handle this
                        return;
                    default:
                        D.Err($"Unhandled scan found mode in starmap menu: {sys.Mode.Value}");
                        return;
                }

                w0.SetPercent(1f, false);
                w.SetText2(string.Empty);
            });

            sys.ScanProgress.Bind(lifetime, (pct) => {
                if (sys.ScanProgress < 0f) {
                    w0.SetPercent(0f, false);
                    w0.Item.SetText2(string.Empty);
                } else {
                    w0.SetPercent(pct);
                }
            });

            sys.MatTarget.Bind(lifetime, (mat) => {
                if (sys.Mode.Value != ScanMode.Mat) { return; }
                if (mat == null) { return; }
                w.SetImage(mat?.IconId ?? IconId.CCommunications);
                w.SetText(T.ScanTarget_V1.F(mat.NameT));
            });
        }

        private string ScanTargetNameFor(ScanMode mode, MatType matType = null) {
            switch (mode) {
                case ScanMode.None:
                    return T.ScanTargetNone;
                case ScanMode.HyperjumpRelay:
                    return T.SpaceObjectHyperjumprelay;
                case ScanMode.SpaceStation:
                    return T.SpaceObjectSpacestation;
                case ScanMode.Anomaly:
                    return T.SpaceObjectAnomaly;
                case ScanMode.DerelictShip:
                    return T.ScanTargetDerelictShip;
                case ScanMode.Mat:
                    return matType.NameT;
                default:
                    D.Err($"Unhandled scan found mode in starmap menu: {mode}");
                    return T.Unknown;
            }
        }

        private string ShowScanTooltip() {
            var sys = S.Sys.Scan;
            if (sys.Mode.Value == ScanMode.None) { return null; }
            if (sys.ScanProgress >= 1f) {
                if (sys.FoundSector != null) {
                    if (sys.Mode.Value != ScanMode.DerelictShip) {
                        if (S.Universe.Player.Parent == sys.FoundSector.SO.Parent) {
                            return T.ScanFoundOnPlanet_V2.F(sys.ScanSubject, sys.FoundSector.Name);
                        } else {
                            return T.ScanFoundOnPlanet_V2.F(sys.ScanSubject, sys.FoundSector.SO.ParentPath());
                        }
                    }
                }
            } else {
                if (A.IsInHyperspace) {
                    return Texts.Yellow(T.ScanHyperspaceWarning);
                }
                return T.ScanStats_V2.F(sys.ScanTime, sys.ScanStrength);
            }
            return null;
        }

        private void ToggleScanChoice(WBase owner, bool forceShow) {
            var p = parent.SubPanel;
            if (subPanelLifetime != null) {
                subPanelLifetime.Dispose();
            }
            subPanelLifetime = lifetime.CreateChild("SubPanel");
            isShowingDeepFocus = false;
            p.SetTitle(T.ResearchScanning);

            int idx = 0;

            idx = SpawnResultBlock(idx);
            idx = SpawnSetTargetBlock(idx);
            idx = SpawnDevBlock(idx);

            if (!forceShow || !p.IsShowing) {
                p.Show(owner);
            }
        }

        private int SpawnDevBlock(int idx) {
            if (!A.InstantBuild) { return idx; }
            var p = parent.SubPanel;
            var bIdx = 0;
            var bl = UISpawn.Widget<WContentBlock>(subPanelLifetime,
                UIPrefabId.WContentBlock, p.Container.Rect, idx++);

            var label = UISpawn.Widget<WText>(subPanelLifetime,
                UIPrefabId.WContent2Header, bl.Container, bIdx++);
            label.Load("Dev Tools");

            var btnSet99Pct = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                UIPrefabId.WContent2Button, bl.Container, bIdx++);
            btnSet99Pct.Load(IconId.WDev, "Set scan progress to 99%", null,
                () => {
                    S.Sys.Scan.ScanProgress.Set(0.99f);
                    parent.SubPanel.Close();
                });
            return idx;
        }

        private int SpawnSetTargetBlock(int idx) {
            var p = parent.SubPanel;
            var sys = S.Sys.Scan;
            var mode = sys.Mode.Value;
            var bl = UISpawn.Widget<WContentBlock>(subPanelLifetime,
                UIPrefabId.WContentBlock, p.Container.Rect, idx++);

            var bIdx = 0;

            var label = UISpawn.Widget<WText>(subPanelLifetime,
                UIPrefabId.WContent2Header, bl.Container, bIdx++);
            label.Load(T.SelectTarget);

            // resource
            var btnMat = UISpawn.Widget<WContentListButton>(subPanelLifetime,
                UIPrefabId.WContent2ListButton, bl.Container, bIdx++);
            btnMat.Load(IconId.CPickaxe, T.ScanTargetResource, null);
            btnMat.LoadOnToggle(subPanelLifetime, ToggleMatList, expandResources);

            // space station
            var btnSpaceStation = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                UIPrefabId.WContent2Button, bl.Container, bIdx++);
            btnSpaceStation.Load(IconId.CTrade, T.SpaceObjectSpacestation, null,
                () => {
                    sys.SetMode(ScanMode.SpaceStation, null);
                    parent.SubPanel.Close();
                });

            // hyperjump relay
            var btnRelay = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                UIPrefabId.WContent2Button, bl.Container, bIdx++);
            btnRelay.Load(IconId.CHyperspaceEnter, T.SpaceObjectHyperjumprelay, null,
                () => {
                    sys.SetMode(ScanMode.HyperjumpRelay, null);
                    parent.SubPanel.Close();
                });

            if (S.BB.Get(BBKeyH.SystemScanAdvanced, false)) {
                // anomaly
                var btnAnomaly = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                    UIPrefabId.WContent2Button, bl.Container, bIdx++);
                btnAnomaly.Load(IconId.CWarning, T.SpaceObjectAnomaly, null,
                    () => {
                        sys.SetMode(ScanMode.Anomaly, null);
                        parent.SubPanel.Close();
                    });

                // derelict ship
                var btnDerelict = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                    UIPrefabId.WContent2Button, bl.Container, bIdx++);
                btnDerelict.Load(GraphicsId.SpaceObjectsDerelictsShipFighter01_Derelict_L,
                    T.ScanTargetDerelictShip, null,
                    () => {
                        sys.SetMode(ScanMode.DerelictShip, null);
                        parent.SubPanel.Close();
                    });
            }

            // stop scanning
            if (mode != ScanMode.None) {
                var btnNone = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                    UIPrefabId.WContent2Button, bl.Container, bIdx++);
                btnNone.Load(IconId.CStop, T.Cancel, null, () => {
                    S.Sys.Scan.SetMode(ScanMode.None, null);
                    parent.SubPanel.Close();
                });
            }
            return idx;
        }

        private int SpawnResultBlock(int idx) {
            var sys = S.Sys.Scan;
            var mode = sys.Mode.Value;
            var so = sys.FoundSO.Value;
            if (so == null) { return idx; }

            int bIdx = 0;
            var bl = UISpawn.Widget<WContentBlock>(subPanelLifetime,
                UIPrefabId.WContentBlock, parent.SubPanel.Container.Rect,
                idx++);
            bIdx = 0;

            // has result
            var label = UISpawn.Widget<WText>(subPanelLifetime,
                UIPrefabId.WContent2Header, bl.Container, bIdx++);
            label.Load(T.NounScanResult);

            var btnGoto = UISpawn.Widget<WButtonIconText2>(subPanelLifetime,
                UIPrefabId.WContent2Button, bl.Container, bIdx++);
            var name = T.ScanFoundOnPlanet_V2.F(
                ScanTargetNameFor(mode, sys.MatTarget.Value), so.DisplayName);
            btnGoto.Load(so.Icon, name, null, () => {
                S.Sys.Universe.ShowInStarmap(so);
                parent.SubPanel.Close();
            });

            return idx;
        }

        private bool ToggleMatList(Lifetime life, RectTransform rt, bool willShow, bool wasLoaded) {
            expandResources = willShow;
            if (!willShow || wasLoaded) { return wasLoaded; }

            var sys = S.Sys.Scan;
            SpawnMatList(life, rt, 0, (type) => {
                sys.SetMode(ScanMode.Mat, type);
                parent.SubPanel.Close();
                S.UIQuery.HideCodexTooltip.Ask();
            });
            return true;
        }

        // Scannable deposit materials the scanning research allows, then a locked row.
        // Materials above hideAboveRarity are left out entirely, not shown as locked.
        private void SpawnMatList(Lifetime life, RectTransform rt, int idx,
                Action<MatType> onPick, int hideAboveRarity = int.MaxValue) {
            int maxRarity = S.Sys.Scan.MaxScannableRarity();
            int locked = 0;

            foreach (var t in MatType.Known.OrderBy(t => t.Value.NameT)) {
                var type = t.Value;
                if (type.Deposit == null) { continue; }
                if (type.Deposit.ScanDifficulty <= 0f) { continue; }
                if (type.MatRarity > hideAboveRarity) { continue; }
                if (type.MatRarity > maxRarity) {
                    locked++;
                    continue;
                }
                var cnt = S.Sys.Inventory.AvailableCount(type);
                var w = UISpawn.Widget<WButtonIconText2>(life,
                    UIPrefabId.WContent2Button, rt, idx++);
                w.Load(
                    type.IconId, type.NameT, Units.NumColoredAbbr(cnt), () => onPick(type));
                UIUtils.AddCodexTooltip(life, w.gameObject, w, type.Def);
            }

            if (locked > 0) {
                var lw = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContent2Text2, rt, idx++);
                lw.Load(IconId.CLocked, T.ResearchLocked, Units.XNum(locked));
                UIUtils.AddTipFunc(life, lw, ShowLockedTooltip);
            }
        }

        private string ShowLockedTooltip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            var isExtendedScan = S.BB.Get(BBKeyH.SystemScanExtended, false);
            var isAdvancedScan = S.BB.Get(BBKeyH.SystemScanAdvanced, false);
            if (!isAdvancedScan) {
                AppendLocked(sb, 1, 3, DefIdsH.ResearchScanningSystem, T.SystemScanAdvanced);
                sb.Append(GlyphMap.BRHR);
            }
            if (!isExtendedScan) {
                AppendLocked(sb, 4, int.MaxValue,
                    DefIdsH.ResearchScanningExtended, T.SystemScanExtended);
            }
            return sb.ToString();
        }

        private static void AppendLocked(StringBuilder sb, int rFrom, int rTo,
            int defIdH, string name) {
            sb.AppendFormat("{0}: {1}\n",
                Texts.LinkToResearch(defIdH, Texts.Yellow(name)),
                    Texts.Red(T.ResearchLocked));
            var locked = Caches.StringList;
            locked.Clear();
            foreach (var t in MatType.Known.OrderBy(t => t.Value.NameT)) {
                var type = t.Value;
                if (type.Deposit == null) { continue; }
                if (type.Deposit.ScanDifficulty <= 0f) { continue; }
                if (type.MatRarity >= rFrom && type.MatRarity <= rTo) {
                    locked.Add(type.Def.CodexLink);
                }
            }
            sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
            sb.AppendJoin(", ", locked);
        }
    }
}
