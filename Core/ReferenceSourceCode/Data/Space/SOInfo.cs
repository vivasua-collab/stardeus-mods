using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using Unity.Mathematics;

namespace Game.Data.Space {
    public class SOInfo {
        private readonly SpaceObject so;
        public SOInfo(SpaceObject so) {
            this.so = so;
        }
        public string UITooltip(bool forStarmap) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var biome = so.SpaceEntity?.Biome;


            if (forStarmap) {
                if (so.Region != null) {
                    sb.Append(Texts.Title(T.RegionName_V1.F(so.Name)));
                } else if (so.Sector != null) {
                    sb.Append(Texts.Title(T.SectorName_V1.F(so.Name)));
                } else if (so.Type.InheritParentLooks) {
                    sb.Append(Texts.Title(so.Parent.Type.NameT));
                } else {
                    sb.Append(Texts.Title(so.Type.NameT));
                }
            } else {
                sb.AppendFormat("{0}\n", Texts.LinkToStarmap(so));
            }
            if (biome != null) {
                sb.AppendFormat("{0}: {1}\n", T.GameDifficulty,
                        so.SpaceEntity.Biome.DifficultyText);
                if (so.CalculateExplorationState(out var visited, out var total)) {
                    if (total > 0) {
                        if (visited > 0) {
                            sb.Append(T.AdjExplored);
                            sb.Append(": ");
                            sb.Append(Texts.Accented(Units.XOutOfY(visited, total)));
                            sb.Append("\n");
                        } else {
                            sb.Append(T.AdjUnexplored);
                            sb.Append("\n");
                        }
                    }
                }
            }


            // Universe view
            if (so.Region != null) {
                OnHoverRegion(sb, so);
                // Region view
            } else if (so.Sector != null) {
                OnHoverSector(sb, so);
            } else {
                OnHoverLocal(sb, so, false);
            }
            // Universe preview
            if (G.IsDemoMode) {
                // sb.Append("yes?");
                return sb.ToString();
            }
            if (biome != null && A.InstantBuild && !G.IsDemoMode) {
                #if UNITY_EDITOR
                // sb.AppendFormat("\n\nDEV: Visited: {0}/{1}(hs)\n", so.IsVisited, so.IsVisitedHS);
                // sb.AppendFormat(D.DumpInstance(so));
                #endif
                biome.DebugTooltip(sb);
                sb.Append("\n");
            }
            var str = sb.ToString();
            return str;
        }

        private void OnHoverLocal(StringBuilder sb, SpaceObject targetSO, bool addName) {
            // Sector view
            if (targetSO.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                return;
            }
            if (targetSO.Ship?.AppendTooltip(sb, false) == true) {
                return;
            }
            if (targetSO.Type.IdH == SpaceObjectTypeIdH.ShipHyperTrail) {
                var trail = targetSO.S.Query.FindHyperTrail.Ask(targetSO);
                trail?.LoadInfo(sb);
                return;
            }
            if (so.S.Sys.Planets.TryGetResourceFor(targetSO, out var res, out var mt)) {
                var avail = so.S.Sys.Inventory.AvailableCount(mt);
                sb.Append(Texts.Accented(mt.NameT));
                sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
                sb.AppendLine(T.AvailableCount_V1.F(Units.NumColoredAbbr(avail)));
                var diff = T.MiningDifficultyValue_V1.F(
                    Texts.Accented(res.MiningDifficulty.ToString("0.#")));
                if (mt.Deposit.IsWithDepth) {
                    var depth = $"{GlyphMap.ArrowDown}{Texts.Accented(Units.Depth(res.MiningDepth))}";
                    diff = $"{diff} {depth}";
                }
                if (res.Unretrieved >= 1f && res.UnretrievedMax > 0) {
                    var unrInt = (int) math.ceil(res.Unretrieved);
                    var cap = Units.XOutOfY(unrInt, res.UnretrievedMax);
                    if (addName) {
                        sb.AppendFormat(" - <b>{0}</b> ({1}). {2}\n",
                            mt.Def.CodexLink, Texts.Green(cap), diff);
                    } else {
                        sb.AppendFormat("({0}). {1}\n", Texts.Green(cap), diff);
                    }
                } else {
                    if (addName) {
                        sb.AppendFormat(" - <b>{0}</b> {1}\n", mt.Def.CodexLink, diff);
                    } else {
                        sb.Append(diff);
                        sb.Append("\n");
                    }
                }
            }
        }

        private void OnHoverSector(StringBuilder sb, SpaceObject targetSO) {
            ListShips(sb, targetSO);
            var mats = targetSO.Knowledge.Mats;
            if (A.InstantBuild) {
                mats = targetSO.Mats?.Select(m => m.IdH).ToList();
            }
            int unknownDeposits = 0;
            if (!Arrays.IsEmpty(mats)) {
                // sb.Append("\n");
                sb.AppendLine(T.KnownResourceDeposits);
            }
            if (targetSO.IsVisited && targetSO.HasChildren) {
                // resource deposits
                foreach (var child in targetSO.Children) {
                    if (child.IsShip || child.Type.IdH == SpaceObjectTypeIdH.ShipHyperTrail) {
                        continue;
                    }
                    if (child.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                        unknownDeposits++;
                    } else {
                        if (child.Type.IdH != SpaceObjectTypeIdH.Player) {
                            OnHoverLocal(sb, child, true);
                        }
                    }
                }
            } else {
                // we only look at knowledge
                if (!Arrays.IsEmpty(mats)) {
                    foreach (var m in mats) {
                        var mat = MatType.FastGet(m);
                        if (mat != null) {
                            sb.AppendFormat("- <b>{0}</b>\n", mat.Def.CodexLink);
                        }
                    }
                }
            }
            var POIs = targetSO.Knowledge.POIs;
            if (A.InstantBuild) {
                POIs = targetSO.POIs;
            }
            bool addedOtherLabel = false;
            AddBlackHoleInfo(sb, targetSO.Parent, true, ref addedOtherLabel, targetSO.Sector);
            if (!addedOtherLabel && (unknownDeposits > 0 || POIs?.Count > 0)) {
                sb.Append("\n");
                sb.Append(T.FeedbackOther);
                sb.Append(':');
                sb.AppendLine();
            }
            if (unknownDeposits > 0) {
                AppendWithCount(sb, T.ResourceDepositPossible, unknownDeposits);
            }
            if (!Arrays.IsEmpty(POIs)) {
                foreach (var poi in POIs) {
                    AppendPOI(sb, poi.Key, poi.Value);
                }
            }
            if (targetSO.IsVisited) {
                bool hasEffects = false;
                foreach (var fx in targetSO.SpaceEntity.Biome.Effects) {
                    var name = fx.Key.Effect.Name;
                    if (name != null) {
                        if (!hasEffects) {
                            sb.AppendLine();
                            hasEffects = true;
                        }
                        sb.Append(GlyphMap.SpriteWarning);
                        sb.Append(' ');
                        sb.AppendLine(Texts.Accented(name));
                    }
                }
            }
        }

        private void ListShips(StringBuilder sb, SpaceObject targetSO) {
            if (targetSO.Sector == null) {
                D.Err("Trying to list ships in SO that is not a sector: {0}",
                    targetSO);
                return;
            }
            var ships = targetSO.S.Query.NPCShipsAt.Ask(targetSO.Sector);
            if (!SOUtils.HasNonPlayerShips(ships)) { return; }
            sb.AppendFormat("{0}:", T.NounShips);
            int i = 0;
            var shipC = ships.Count;
            for (int i1 = 0; i1 < shipC; i1++) {
                var ship = ships[i1];
                if (ship.IsPlayer) { continue; }
                sb.AppendLine();
                ship.AppendTooltip(sb, true);
                if (++i == 19) {
                    break;
                }
            }
            if (shipC > i) {
                sb.AppendFormat(Texts.Grey("\n+{0}"), shipC - i);
            }
            sb.AppendLine();
        }

        private void AppendPOI(StringBuilder sb, int typeIdH, int count) {
            var type = SpaceObjectType.Find(typeIdH);
            if (type == null) { return; }
            if (count > 1) {
                sb.AppendFormat(" - <b>{0}</b> x{1}\n", type.NameT, count);
            } else {
                sb.AppendFormat(" - <b>{0}</b>\n", type.NameT);
            }
        }

        private void OnHoverRegion(StringBuilder sb, SpaceObject targetSO) {
            if (UIShowing.MainMenu) {
                sb.Append(Texts.SubTitle(T.KnownResourceDeposits));
                foreach (var dep in targetSO.Region.Biome.Deposits.Keys
                        .OrderBy(d => d.Mat.MatRarity).ThenBy(d => d.Mat.NameT)) {
                    var mat = dep.Mat;
                    var name = mat.MatRarity < 2 ? mat.NameT : "???";
                    sb.AppendFormat("{0} {1}\n", Texts.Accented(GlyphMap.ArrowRight), name);
                }
                sb.AppendLine(Texts.Grey(T.RadarDepositsTip));
            }

            var isIsloated = true;
            foreach (var link in targetSO.Region.Links) {
                if (!SpaceLink.IsIsolatedRegion(link)) {
                    isIsloated = false;
                    break;
                }
            }
            if (isIsloated) {
                sb.AppendFormat("{0}: {1}\n", Texts.Red(T.Warning),
                    T.IsolatedRegionTip_V2.F(
                        Texts.Accented(T.SpaceObjectHyperjumprelay),
                        G.IsDemoMode ? Texts.Accented(T.ObjectsDevicesFtldrive) :
                            Texts.LinkToCodex(DefIdsH.ObjectsDevicesFTLDrive)));
            }

            if (G.IsDemoMode) {
                return;
            }

            // Not in the new game universe preview: there is no running game to ask yet
            if (!UIShowing.MainMenu && RegionSupplyInfo.Append(targetSO.S, sb, targetSO.Region)) {
                sb.Append("\n");
            }

            // loop through children and list what we know about this region
            var matCounts = Caches.MatCounts;
            if (LoadKnownRegionMats(targetSO.Region, matCounts) > 0) {
                sb.Append("\n");
                sb.AppendFormat("{0}:\n", T.KnownResourceDeposits);
                foreach (var mat in matCounts) {
                    if (mat.Value > 1) {
                        sb.AppendFormat("- <b>{0}</b> {1}\n", mat.Key.Def.CodexLink,
                            Texts.Grey(Units.XNum(mat.Value)));
                    } else {
                        sb.AppendFormat("- <b>{0}</b>\n", mat.Key.Def.CodexLink);
                    }
                }
            }
            bool addedOtherLabel = false;
            foreach (var sector in targetSO.Region.Sectors) {
                AddBlackHoleInfo(sb, targetSO, true, ref addedOtherLabel, sector);
            }
            if (A.InstantBuild) {
                ListRegionShips(sb, targetSO);
            }
        }

        private static void AddBlackHoleInfo(StringBuilder sb, SpaceObject region,
                bool addOtherLabel, ref bool addedOtherLabel, SpaceSector sector) {
            // can be null in universe preview
            if (sector?.SO == null) { return; }
            // if (!sector.SO.IsVisited) { return; }
            var tplId = sector.Biome.Template.IdH;
            if (tplId != SpaceEntityTemplateIdH.BlackHole
                    && tplId != SpaceEntityTemplateIdH.DarkPortal) {
                return;
            }
            if (!sector.SO.HasChildren) { return; }
            foreach (var child in sector.SO.Children) {
                var ctId = child.Type.IdH;
                if (ctId != SpaceObjectTypeIdH.BlackHole
                        && ctId != SpaceObjectTypeIdH.DarkPortal) {
                    continue;
                }
                if (addOtherLabel && !addedOtherLabel) {
                    sb.AppendFormat("{0}:", T.FeedbackOther);
                    addedOtherLabel = true;
                }
                if (ctId == SpaceObjectTypeIdH.BlackHole) {
                    var leadsTo = SOActionEnterBlackHole.LeadsTo(region.S, child);
                    sb.AppendFormat("\n - {0}: {1}",
                        Texts.Accented(T.SpaceObjectBlackhole),
                        leadsTo);
                } else if (ctId == SpaceObjectTypeIdH.DarkPortal) {
                    sb.AppendFormat("\n - {0}",
                        Texts.Accented(T.SpaceObjectDarkportal));
                }
            }
        }

        private void ListRegionShips(StringBuilder sb, SpaceObject targetSO) {
            bool hasShipsLabel = false;
            foreach (var sec in targetSO.Region.Sectors) {
                var ships = so.S.Query.NPCShipsAt.Ask(sec);
                if (!Arrays.IsEmpty(ships)) {
                    if (!hasShipsLabel) {
                        sb.AppendFormat("\n{0}:", T.NounShips);
                        hasShipsLabel = true;
                    }
                    foreach (var ship in ships) {
                        sb.AppendLine();
                        ship.AppendTooltip(sb, true);
                    }
                }
            }
            if (hasShipsLabel) {
                sb.AppendLine();
            }
        }
        public int LoadKnownRegionMats(SpaceRegion region, Dictionary<MatType, int> matCounts) {
            matCounts.Clear();
            foreach (var sec in region.Sectors) {
                // sec.SO can be null in new game preview gen
                if (sec.SO != null && !Arrays.IsEmpty(sec.SO.Knowledge.Mats)) {
                    if (!Arrays.IsEmpty(sec.SO.Mats)) {
                        foreach (var mat in sec.SO.Mats) {
                            if (sec.SO.HasKnowledgeOf(mat)) {
                                matCounts.GetIncrement(mat, 1);
                            }
                        }
                    }
                }
            }
            return matCounts.Count;
        }

        private void AppendWithCount(StringBuilder sb, string str, int cnt) {
            if (cnt <= 0) { return; }
            if (cnt > 1) {
                sb.AppendFormat("- <b>{0}</b> x{1}\n", str, cnt);
            } else {
                sb.AppendFormat("- <b>{0}</b>\n", str);
            }
        }
    }
}
