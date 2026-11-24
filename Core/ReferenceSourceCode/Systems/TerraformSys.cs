#if !PRODUCTION_BUILD
// #define DEBUG_TERRAFORMING_TARGET
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Story.Events;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Notify;
using KL.Collections;

namespace Game.Systems {
    public sealed class TerraformSys : GameSystem, IOverlayProvider {
        public const string SysId = "TerraformSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TerraformSys());
        }
        public List<OverlayInfo> Overlays { get; } = new();

        public readonly List<SpaceObject> KnownTerraformable = new();
        public SpaceObject Target;
        private SpaceObject lastTarget;
        public Entity Entity => null;

        protected override void OnInitialize() {
            Overlays.Clear();
            Overlays.Add(new OverlayInfo(OverlayType.Controls,
                OverlayId.Terraform, IconId.WTerraform));
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Sig.AfterLoadState.AddListener(OnAfterLoad);
            S.Sig.FoundTerraformable.AddListener(OnFoundTerraformable);
            S.Sig.TerraformProgress.AddListener(OnTerraformProgress);
            S.Sig.SpaceObjectEnter.AddListener(OnSOEnter);
        }

        private void OnSOEnter(SpaceObject to, SpaceObject from) {
            OnSpaceObjectNearby(to, true);
        }

        private NotificationHandle dumbActionEvent;

        private void OnTerraformProgress(TerraformType t, float p) {
            CheckTarget();
            if (Target == null) {
                if (!dumbActionEvent.IsValid) {
                    dumbActionEvent = Notification.Build(IconId.CWarning,
                            T.TerraformingIneffective, Consts.TicksPerDay)
                        .WithOnClick(() => {
                            The.Dialog.ShowSimple(IconId.CComputing,
                                T.TerraformingIneffectiveInfo);
                        })
                        .Show();
                }
                return;
            } else {
                Notification.Remove(ref dumbActionEvent);
            }
            var progNow = Target.AddTerraformProgress(t, p, out var justFinished);
            if (S.Universe.Player.Parent == Target) {
                // we are supposed to update the closeup
                foreach (var so in Target.Children) {
                    if (so.Type.IdH == SpaceObjectTypeIdH.PlanetCloseup) {
                        so.UI?.UpdateLooks();
                        so.Visual?.UpdateLooks();
                        break;
                    }
                }
            }
            if (justFinished) {
                Notification.Build(t.IconId,
                        T.TerraformTypeCompleted_V1.F(t.NameT), Consts.TicksPerDay, NotificationType.Immediate)
                    .WithOnClick(() => {
                        S.UIQuery.ToggleTerraformConfig.Ask(true);
                    })
                    .Show();

                The.AchievementTracker.Unlock(t.AchievementId);
                if (Target.TerraformingProgress >= 1f) {
                    The.AchievementTracker.Unlock(
                        Achievements.AchievementTerraformComplete.AchievementId);
                    The.Dialog.ShowSimple(IconId.CComputing,
                        T.TerraformCompleted_V1.F(Target.StarmapLink));
                }
                if (UIShowing.Terraforming) {
                    S.UIQuery.RebuildTerraformConfig.Ask();
                }
            } else {
                S.Sig.TerraformUpdated.Send(t, progNow);
                S.UIQuery.RebuildTerraformConfig.Ask();
            }
        }

        public void CheckTarget() {
            if (Target != null) {
                var pl = S.Universe.Player;
                // We are in the planet view
                if (Target != pl.Parent) {
                    // left the start system
                    Target = null;
                }
            }
        }

        private void OnSpaceObjectNearby(SpaceObject obj, bool added) {
            CheckTarget();
            // a workaround to fix a bug where for some reason found planets
            // don't get added to the list
            if (obj.HasTerraformable && obj.Type.IsTerraformable
                    && !KnownTerraformable.Contains(obj)) {
                KnownTerraformable.Add(obj);
                if (UIShowing.Terraforming) {
                    S.UIQuery.RebuildTerraformConfig.Ask();
                }
            }
            if (!added || !obj.HasTerraformable || !KnownTerraformable.Contains(obj)) {
                return;
            }

            Target = obj;
            if (UIShowing.Terraforming) {
                S.UIQuery.RebuildTerraformConfig.Ask();
            }

            // Update all terraformers to show the right progress
            foreach (var t in TerraformType.All) {
                S.Sig.TerraformUpdated.Send(t, Target.TerraformingProgressFor(t));
            }
        }

        private void OnFoundTerraformable(SpaceObject obj) {
            if (!obj.HasTerraformable) {
                D.Err("Found terraformable that is not a target! {0}", obj);
                return;
            }
            KnownTerraformable.Add(obj);
            if (UIShowing.Terraforming) {
                S.UIQuery.RebuildTerraformConfig.Ask();
            }
        }

        private void OnAfterLoad(GameState s) {
            RefreshTarget(s);
        }

        public void RefreshTarget(GameState s) {
            foreach (var obj in s.Universe.ObjectsById) {
                var ob = obj.Value;
                if (ob.HasTerraformable && ob.Sector != null) {
                    KnownTerraformable.AddUniqueInefficient(ob);
                    if (ob == s.Universe.Player.Parent) {
                        Target = ob;
                    }
                }
            }
        }

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!on || info.Id != OverlayId.Terraform) {
                return;
            }
            S.UIQuery.ToggleTerraformConfig.Ask(true);
        }

        public bool HasControls() {
            var lastPing = S.BB.Get(BBKeyH.TerraformControlsPing, 0L);
            return G.Ticks - lastPing <= Consts.TicksPerHour;
        }

        public override void Unload() {
        }

        public void BeginSettleIn() {
            if (!CanBeginSettleIn()) {
                UISounds.PlayActionDenied();
                return;
            }
            if (UIShowing.Dialog) {
                The.Dialog.Hide();
            }
            A.S.UIQuery.ToggleTerraformConfig.Ask(false);
            The.Dialog.ShowChoice(IconId.CComputing,
                T.SettleInConfirm_V1.F(Target.StarmapLink), null, () => {
                    if (!CanBeginSettleIn()) { return; }
                    S.Clock.SetSpeed(ClockSpeed.Normal);
                    new ColonizePlanetCutscene(S, Target, () => {
                        S.Clock.SetSpeed(ClockSpeed.Normal);
                    });
                }, T.Cancel, T.Confirm, canCloseWithEsc: false);
        }

        public bool CanBeginSettleIn() {
            CheckTarget();
            if (Target == null) {
                return false;
            }
            var reasons = new List<string>();
            if (S.Sys.Expedition.Expeditions.Count > 0) {
                reasons.Add(T.SettleInExpeditions);
            }
            if (S.Sys.Combat.IsAnyCombatInProgress) {
                reasons.Add(T.SettleInCombat);
            }

            if (reasons.Count > 0) {
                WPopup.Create(false, IconId.CWarning, T.OperationFailed, string.Join("\n", reasons)).Spawn();
                return false;
            }
            return true;
        }
    }
}
