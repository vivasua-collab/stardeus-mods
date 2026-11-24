using System.Collections.Generic;
using System.Linq;
using Game.Achievements;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Procgen.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class VoidRipperAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new VoidRipperAction());
        }

        public const string ActionId = "VoidRipper";
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanUse(s, obj, out _, out _)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(s, l, p, i, obj), Priority.Normal, 50);
            return true;
        }

        // Only available for single selection
        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            data = EntityActionData.Unavailable;
            return false;
        }

        private WBase Create(GameState s, Lifetime l, RectTransform p, int i, Obj obj) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CBlackHole, T.GoThrough, () => ShowQuickChoice(s, obj, widget));
            return widget;
        }

        private static void ShowQuickChoice(GameState s, Obj obj, WBase anchor) {
            if (!CanUse(s, obj, out var ripper, out var hole)) { return; }

            var dest = SOActionEnterBlackHole.LeadsTo(s, hole);
            var qc = QuickChoice.Create($"{T.ObjectsDevicesVoidripper}: {T.GoThrough}");
            qc.Widget = anchor.Rect;
            // A warp leaves derelicts behind like a hyperjump, so it warns like one (STA-1891)
            qc.AddButton($"{T.GoThrough} ({dest})", () => SOUtils.CheckAbandonDerelicts(
                () => GoThrough(s, ripper, hole), skipCheckIfHasQueue: false));
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private static void GoThrough(GameState s, VoidRipperComp ripper, SpaceObject hole) {
            if (!ripper.CanDischarge()) {
                WPopup.Warning(T.OperationFailed, T.VoidRipperRequired);
                return;
            }
            if (!s.Research.IsUnlocked(DefIdsH.ResearchSpaceTravelBlackHoles)) {
                s.Sig.ShowErrorMessage.Send(
                    $"{T.RequiredDeviceIsNotAvailable}: {The.Defs.FastGet(DefIdsH.ObjectsDevicesVoidRipper).CodexLink}");
                return;
            }

            var leadsToHoleId = hole.Parent.Data.GetInt("LeadsToId", 0);
            if (leadsToHoleId == 0) {
                GenSpaceObjectSectorBlackHole.BuildNetwork(s);
                leadsToHoleId = hole.Parent.Data.GetInt("LeadsToId", 0);
                if (leadsToHoleId == 0) {
                    D.Err("Failed generating black hole network?");
                    WPopup.Warning(T.OperationFailed, "BUG: failed generating black hole network");
                    return;
                }
            }
            hole.Parent.Knowledge.SetInt("LeadsToId", leadsToHoleId);
            var leadsTo = s.Universe.Find(leadsToHoleId);
            // Discharged only once the warp happened: an Asimov law can still refuse to cut
            // a derelict loose, and that must not cost the charge
            if (SOUtils.WarpTo(s, hole, leadsTo)) {
                ripper.TryDischarge();
                s.Situation.BlackHoleJumps++;
                s.Sig.ShowFanfare.Send(T.StarmapBlackholeWarping_V1.Replace("200%", "125%").F(
                    $"{T.RegionName_V1.F(hole.Parent.Parent.StarmapLink)} {Texts.Accented(GlyphMap.ArrowRight)} {T.RegionName_V1.F(leadsTo.Parent.StarmapLink)}"));
                D.Warn("Entered black hole: {0} -> {1}", hole, leadsTo);
                The.AchievementTracker.Unlock(AchievementVoidRipper.AchievementId);
            }
        }

        private static bool CanUse(GameState s, Obj obj, out VoidRipperComp ripper, out SpaceObject hole) {
            ripper = null;
            hole = null;
            if (!obj.IsActive || !obj.TryGetComponent(out ripper)) { return false; }
            if (!ripper.CanDischarge() && !A.InstantBuild) { return false; }
            hole = s.Sys.Ship.FirstReachableNearby(SpaceObjectTypeIdH.BlackHole);
            return hole != null;
        }
    }
}
