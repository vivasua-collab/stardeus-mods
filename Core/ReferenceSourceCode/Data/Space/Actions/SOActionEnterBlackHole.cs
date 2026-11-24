using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Achievements;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Input;
using Game.Procgen.Space;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Space {
    public sealed class SOActionEnterBlackHole : SOAction {
        public const string ActionId = "EnterBlackHole";
        [SelfInit]
        private static void RegisterAction() {
            Register(ActionId, new SOActionEnterBlackHole());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return //ctx.SO.IsIdentified
                ctx.SO.Type.IdH == SpaceObjectTypeIdH.BlackHole
                && ctx.S.Sys.Ship.IsNearby(ctx.SO);
        }

        public static string LeadsTo(GameState s, SpaceObject hole) {
            var leadsTo = hole.Parent.Knowledge.GetInt("LeadsToId", 0);
            if (leadsTo != 0) {
                var destSO = s.Universe.Find(leadsTo);
                return SOUtils.RegionSectorName(destSO.Sector);
            }
            return T.Unknown;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.GoThrough, GoThrough, LeadsTo(ctx.S, ctx.SO));
            return true;
        }

        private void GoThrough() {
            if (!IsAvailable(ctx)) {
                WPopup.Warning(T.OperationFailed, T.OperationFailed);
                return;
            }
            if (!ctx.S.Research.IsUnlocked(DefIdsH.ResearchSpaceTravelBlackHoles)) {
                ctx.S.Sig.ShowErrorMessage.Send($"{T.RequiredDeviceIsNotAvailable}: {The.Defs.FastGet(DefIdsH.ObjectsDevicesVoidRipper).CodexLink}");
                return;
            }

            var rippers = ctx.S.Components.AllByType<VoidRipperComp>();
            VoidRipperComp ripper = null;
            if (rippers != null) {
                ripper = (VoidRipperComp) rippers
                    .Where(r => ((VoidRipperComp) r).CanDischarge())
                    .OrderBy(r => ctx.S.Rng.Value)
                    .FirstOrDefault();
            }
            if (ripper == null && !A.InstantBuild) {
                WPopup.Warning(T.OperationFailed, T.VoidRipperRequired);
                return;
            }
            // the action is a shared instance and hovering another object reloads its context
            var c = ctx;
            // A warp leaves derelicts behind like a hyperjump, so it warns like one (STA-1891)
            SOUtils.CheckAbandonDerelicts(() => Jump(c, ripper), skipCheckIfHasQueue: false);
        }

        // The ripper discharges only once the warp happened: an Asimov law can still refuse
        // to cut a derelict loose, and that must not cost the charge
        private static void Jump(SOActionContext c, VoidRipperComp ripper) {
            if (ripper?.CanDischarge() == false && !A.InstantBuild) {
                WPopup.Warning(T.OperationFailed, T.VoidRipperRequired);
                return;
            }
            var leadsToHoleId = c.SO.Parent.Data.GetInt("LeadsToId", 0);
            if (leadsToHoleId == 0) {
                // Let's generate where it leads to
                GenSpaceObjectSectorBlackHole.BuildNetwork(c.S);
                leadsToHoleId = c.SO.Parent.Data.GetInt("LeadsToId", 0);
                if (leadsToHoleId == 0) {
                    D.Err("Failed generating black hole network?");
                    WPopup.Warning(T.OperationFailed,
                        "BUG: failed generating black hole network");
                    return;
                }
            }
            c.SO.Parent.Knowledge.SetInt("LeadsToId", leadsToHoleId);
            var leadsTo = c.S.Universe.Find(leadsToHoleId);
            var from = c.SO.Parent.Parent;
            if (SOUtils.WarpTo(c.S, c.SO, leadsTo)) {
                ripper?.TryDischarge();
                c.S.Situation.BlackHoleJumps++;
                c.S.Sig.ShowFanfare.Send(T.StarmapBlackholeWarping_V1.Replace("200%", "125%").F(
                    $"{T.RegionName_V1.F(from.StarmapLink)} {Texts.Accented(GlyphMap.ArrowRight)} {T.RegionName_V1.F(leadsTo.Parent.StarmapLink)}"));
                D.Warn("Entered black hole: {0} -> {1}", c.SO, leadsTo);
                // Hack to rebuild the nebula
                The.AchievementTracker.Unlock(AchievementVoidRipper.AchievementId);
            }
        }


        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
