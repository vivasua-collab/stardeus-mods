#if !PRODUCTION_BUILD
// #define DEBUG_REGIONS
// #define DEV_TOOLTIP
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Atmo;
using Game.Systems.Creatures;
using Game.Systems.Reg;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectEnvHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectEnv";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectEnvHandler());
        }

        public override int Order => 0;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
        }

        public override void OnHover(Vector2 pos) {
            if (ctx.IsAltPressed || ctx.CurrentOverlay?.NeedsEnvTooltip == true) {
                AppendEnvTooltip();
            }
        }
        public void AppendEnvTooltip() {
            var pos = ctx.HoveredPos;
            if (Pos.IsOOB(pos)) { return; }
            var sb = ctx.BeginTooltipBlock();
            var txt = T.Environment;
            if (The.Bindings.HasBinding(ActionType.AltModifier)) {
                txt = txt + " " + The.Bindings.GetBinding(ActionType.AltModifier)
                        .AllControlGlyphs(out _);
            }
            sb.Append(Texts.SubTitle(txt));
            #if DEV_TOOLTIP
            sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, The.Bindings.Mouse.WorldPosition);
            #endif
            sb.AppendFormat(" {0} {1}", GlyphMap.ArrowRight, S.Sys.Path.DescriptionOf(pos));
            AppendO2(pos, sb);
            AppendTemp(pos, sb);
            AppendO2Res(pos, sb);
            AppendTempRes(pos, sb);
            AppendLight(pos, sb);
            AppendDirt(pos, sb);


            if (S.Sys.Combat.IsHostileCombatInProgress) {
                // combat enables special cover tooltip
                var cover = S.Query.CoverSlotAt.Ask(pos);
                cover.AppendDetails(sb, T.OverlayTipCover, true);
            }

            var isleId = S.Sys.Areas.IslandAt(pos);
            if (isleId == 0) {
                sb.AppendFormat("\n {0} {1}: <b>{2}</b>", GlyphMap.ArrowRight, T.Section, T.OpenSpace);
                #if DEV_TOOLTIP
                var areaId = S.Sys.Areas.At(pos);
                var area = S.Sys.Areas.AreaById(areaId);
                sb.AppendFormat("\n * <b>{1}</b> ({2})",
                    T.RoomId_V1.F(areaId), T.AreaSize_V1.F(area.Count));
                #endif
            } else {
                var isleSize = S.Sys.Areas.Islands[isleId].Count;
                sb.AppendFormat("\n {0} <b>{1}</b> ({2})",
                    GlyphMap.ArrowRight,
                    T.IslandNr_V1.F(isleId), T.AreaSize_V1.F(isleSize));
            }
            if (isleId != 0) {
                var areaId = S.Sys.Areas.At(pos);
                if (areaId > 0) {
                    var area = S.Sys.Areas.AreaById(areaId);
                    sb.AppendFormat("\n {0} <b>{1}</b> ({2})",
                        GlyphMap.ArrowRight,
                        T.RoomId_V1.F(areaId), T.AreaSize_V1.F(area.Count));
                    #if DEV_TOOLTIP
                    sb.AppendFormat("\n * [Dev] Windows: {0}",
                        S.Query.NumWindowsInArea.Ask(areaId));
                    #endif
                } else if (areaId < -1) {
                    var area = S.Sys.Areas.OuterAreaById(areaId);
                    var size = area.Count;
                    if (size > 0) {
                        sb.AppendFormat("\n {0} <b>{1}</b> ({2})",
                            GlyphMap.ArrowRight,
                            T.OuterRoomId_V1.F(-areaId), T.AreaSize_V1.F(area.Count));
                    } else {
                        sb.AppendFormat("Outer room with size 0! {0}", areaId);
                    }
                #if DEV_TOOLTIP
                } else {
                    sb.AppendFormat("\n * [Dev] {0}: <b>{1}</b>", T.Room, T.NotEnclosed);
                    sb.AppendFormat("\n * [Dev] RoomId: {0}", areaId);
                #endif
                    //} else if (isleId != 0 && areaId != -1) {
                    // sb.AppendFormat("{0}: <b>{1}</b>\n", T.Room, T.NotEnclosed);
                }
            }

            ctx.CurrentOverlay?.AppendToEnvTooltip?.Invoke(sb, pos);

            #if DEV_TOOLTIP
            AddCombatInfo(sb);
            #endif
            #if DEBUG_REGIONS
            var b = S.Query.GetSelectedBeing.Ask(true);
            if (b != null) {
                sb.AppendFormat("\n * Region Id: {0}", S.Query.RegionAt.Ask(b.PosIdx));
                var dist = S.Query.RegionDist.Ask(RegPathReq.Near(b.PosIdx, pos, b.Motion.IsGrounded));
                sb.AppendFormat("\n * Region Dist: {0}", dist);
                Caches.IntSet2.Clear();
                var areaId = S.Sys.Areas.At(b.PosIdx);
                S.Sys.Areas.OutlineOfArea(areaId, Caches.IntSet2);
                sb.AppendFormat("\n Outline of area: {0}", areaId);
                foreach (var o in Caches.IntSet2) {
                    sb.Append(Pos.String(o));
                    sb.Append(' ');
                }
                var tile = EntityUtils.GetTopmostParentTile(pos);
                if (tile != null) {
                    var closest = RegUtils.FindClosestReachable(S, b.PosIdx,
                        b.Motion.IsGrounded, new Tile[] { tile }, 1, (t) => t.PosIdx, false);
                    if (closest != null) {
                        sb.AppendFormat("\n * Reachable: {0}", closest);
                    }
                }
            }
            #endif
        }

        private void AppendDirt(int pos, StringBuilder sb) {
            var dirtData = S.Sys.Dirt.At(pos);
            var count = Arrays.Count(dirtData);
            if (count > 0) {
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.Dirt, Units.Num(count));
            }
        }

        public override bool AddSelectedBeingRightClickContext(
                    int playerFaction, int canDirectControl, int underDirectControl,
                QuickChoice choice) {
            if (playerFaction == 0) { return false; }
            if (ctx.SelectedBeings.Count != 1) { return false; }
            if (ctx.TargetBeingIsUnderDirectControl) { return false; }
            var dirtData = S.Sys.Dirt.At(ctx.HoveredPos);
            var count = Arrays.Count(dirtData);
            if (count == 0) { return false; }
            var being = ctx.SelectedBeings[0];
            if (!ctx.TargetBeingIsControllable) {
                choice.AddButton(Texts.Strike(T.CleanFloors), null, T.NoDirectControlShort);
            } else if (!being.Data.HasAbility(AbilityIdH.CleanFloors)) {
                // A Manipulator gives the Cleaning job through Clean Device only
                var sb = Caches.StringBuilder;
                sb.Clear();
                BeingUtils.AppendLackingAbilities(sb, being.Data,
                    new FrugalArray<int>(AbilityIdH.CleanFloors));
                choice.AddButton(Texts.Strike(T.CleanFloors), null, sb.ToString());
            } else if (!being.AIAgent.Jobs.CanDo(JobTypeIdH.Cleaning, out string because)) {
                choice.AddButton(Texts.Strike(T.CleanFloors), null, Texts.Red(because));
            } else if (!ctx.TargetBeingCanGoToHoverPos) {
                choice.AddButton(Texts.Strike(T.CleanFloors), null, Texts.Red(T.CannotReachPosition));
                D.Warn("Cannot go to hover pos");
            } else {
                var hovPos = ctx.HoveredPos;
                var hovTile = ctx.HoveredTile;
                if (hovTile != null && hovTile.Goal == null) {
                    choice.AddButton(T.CleanFloors, () => {
                        if (!hovTile.Constructable.CreateCleanGoal(being.AIAgent)) {
                            UISounds.PlayActionDenied();
                        }
                    });
                }
            }
            return true;
        }

        private void AppendLight(int pos, StringBuilder sb) {
            // Light
            var val = S.Query.LightAtPos.Ask(pos);
            var text = Units.Percentage(val);
            if (val < 0.33f) {
                text = Texts.Red(text);
            } else if (val >= 0.75f) {
                text = Texts.Green(text);
            } else {
                text = Texts.Yellow(text);
            }
            sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.Light, text);
        }

        private void AppendTempRes(int pos, StringBuilder sb) {
            // Insulation
            var val = S.GetTempResistance(pos);
            string text;
            if (val < AtmoRes.Space) {
                text = Texts.Red($"0% ({T.NounLeak}!)");
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.HullInsulation, text);
                return;
            } else {
                text = Units.PercentageWithFraction1(val);

            }
            var wallAt = EntityUtils.WallAt(pos);
            if (wallAt != null) {
                if (val < 0.89f) {
                    text = Texts.Red(text);
                } else if (val > 0.99f) {
                    text = Texts.Green(text);
                } else {
                    text = Texts.Yellow(text);
                }
            } else if (EntityUtils.IsFloorAt(pos)) {
                // Still sealed, yellow warns it is damaged on its way to a breach
                text = Units.PercentageWithFraction1(AtmoRes.ForDisplay(val, true));
                if (val > 0.0499f) {
                    text = Texts.Green(text);
                } else {
                    text = Texts.Yellow(text);
                }
            }
            sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.HullInsulation, text);
        }

        private void AppendO2Res(int pos, StringBuilder sb) {
            // Airtightness
            var val = S.GetO2Resistance(pos);
            string text;
            if (val < AtmoRes.Space) {
                text = Texts.Red($"0% ({T.NounLeak}!)");
                sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.HullAirtight, text);
                return;
            } else {
                text = Units.PercentageWithFraction1(val);

            }
            var wallAt = EntityUtils.WallAt(pos);
            if (wallAt != null) {
                if (val < 0.9f) {
                    text = Texts.Red(text);
                } else if (val > 0.999f) {
                    text = Texts.Green(text);
                } else {
                    text = Texts.Yellow(text);
                }
            } else if (EntityUtils.IsFloorAt(pos)) {
                // Still sealed, yellow warns it is damaged on its way to a breach
                text = Units.PercentageWithFraction1(AtmoRes.ForDisplay(val, true));
                if (val > 0.0499f) {
                    text = Texts.Green(text);
                } else {
                    text = Texts.Yellow(text);
                }
            }
            sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.HullAirtight, text);
        }

        private void AppendTemp(int pos, StringBuilder sb) {
            var val = S.GetTemp(pos);
            var text = Texts.Temperature(val);
            var c = Units.KelvinToCelsius(val);
            if (c < -30 || c > 50) {
                text = Texts.Red(text);
            } else if (c < 10 || c > 30) {
                text = Texts.Yellow(text);
            } else {
                text = Texts.Green(text);
            }
            // Temperature
            sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.Temperature, text);
        }

        private void AppendO2(int pos, StringBuilder sb) {
            var o2val = S.GetO2(pos);
            var oxyText = Units.PercentageWithFraction1(o2val);
            if (o2val < Consts.MinOxygen) {
                oxyText = Texts.Red(oxyText);
            } else if (o2val > 0.18f) {
                oxyText = Texts.Green(oxyText);
            } else {
                oxyText = Texts.Yellow(oxyText);
            }
            // Oxygen
            sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight, T.OxygenLevel, oxyText);
        }

        private void AddCombatInfo(StringBuilder sb) {
            if (!S.Sys.Combat.IsHostileCombatInProgress) { return; }
            if (ctx.SelectedBeings.Count == 1) {
                var being = ctx.SelectedBeings[0];
                var hasLOS = EntityUtils.HasLineOfSightCover(being.Position,
                    The.Bindings.Mouse.WorldPosition, 0.5f, CombatUnit.SightRange, out var maxCover);
                // var hasLOS = EntityUtils.HasLineOfSightCoverFast(being.Position,
                //     The.Bindings.Mouse.WorldPosition, 0.5f, 20f);
                // var maxCover = 50;
                sb.AppendFormat("\n * Unit LOS: {0}, MaxCov: {1}",
                    hasLOS, maxCover);
                S.Sig.CameraShowPosLink.Send(null, being.Position,
                    Consts.V2OneTenth, hasLOS ? Color.green : Color.red);
            } else if (ctx.SelectedBeings.Count == 2) {
                var being1 = ctx.SelectedBeings[0];
                var being2 = ctx.SelectedBeings[1];
                var hasLOS = EntityUtils.HasLineOfSightCover(being1.Position,
                    being2.Position, 0.5f, CombatUnit.SightRange, out var maxCover);
                sb.AppendFormat("\n Unit LOS: {0}, MaxCov: {1}",
                    hasLOS, maxCover);
                S.Sig.CameraShowPosLink.Send(being1, being2.Position,
                    Consts.V2OneTenth, hasLOS ? Color.green : Color.red);
            }
        }
    }
}
