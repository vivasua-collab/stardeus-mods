using System;
using System.Collections;
using System.Collections.Generic;
using Game.Achievements;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Input;
using Game.Procgen.Space;
using Game.Systems.Firex;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Space {
    public sealed class SOActionEnterDarkPortal : SOAction {
        public const string ActionId = "EnterDarkPortal";
        [SelfInit]
        private static void RegisterAction() {
            Register(ActionId, new SOActionEnterDarkPortal());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return ctx.SO.Type.IdH == SpaceObjectTypeIdH.DarkPortal
                && ctx.S.Sys.Ship.IsNearby(ctx.SO);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.GoThrough, GoThrough, T.SpaceObjectInfoDarkportal);
            return true;
        }

        private void GoThrough() {
            if (!IsAvailable(ctx)) {
                WPopup.Warning(T.OperationFailed, T.OperationFailed);
                return;
            }

            if (!ctx.S.Sys.ShipNav.CanEnterHyperspaceNow(false, out var failR, UniverseLayer.Region)) {
                ctx.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {failR}");
                return;
            }

            if (ctx.S.CountByDef(The.Defs.FastGet(DefIdsH.ObjectsDevicesPentagramStatue)) > 0) {
                WPopup.Create(false, IconId.CWarning, T.Warning, $"{T.ExitUniverse}?")
                    .WithAction2(T.Cancel, null)
                    .WithAction(T.Confirm, DoGoThrough)
                        .Spawn(true);
            } else {
                ctx.S.Sig.ShowErrorMessage.Send($"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(DefIdsH.ObjectsDevicesPentagramStatue)}");
            }
        }

        private void DoGoThrough() {
            var s = ctx.S;
            var so = ctx.SO;
            Enter(s, so, ctx);
        }

        public static bool Enter(GameState s, SpaceObject so, SOActionContext ctx) {
            if (!s.AllByDef(The.Defs.FastGet(DefIdsH.ObjectsDevicesPentagramStatue),
                    out var pgs)) {
                WPopup.Warning(T.OperationFailed,
                    T.RequiredDeviceIsNotAvailable,
                    Texts.LinkToCodex(DefIdsH.ObjectsDevicesPentagramStatue));
                return false;
            }
            var rand = Rng.UFrom(pgs);
            var statue = rand.Item2;
            if (statue is Tile t) {
                ctx.CloseStarmap();

                EntityUtils.CameraFocusOn(statue);
                if (G.ClockSpeed == ClockSpeed.Stopped) {
                    s.Clock.SetSpeed(ClockSpeed.Normal);
                }
                s.Clock.ForceStop(KL.Clock.ForceStopReason.Cutscene);
                s.Clock.ExecuteAfterFramesForUI(10, () => {
                    try {
                        // TODO: figure out why not this?
                        // t.Flammable.CreateExplosion(true);
                        t.Constructable.BreakDown(1f, false, true, true);
                        Explosion.Create(s, t.Flammable.ExplosionLevel, t.Position, t.PosIdx);
                        UISounds.PlayHyperjump();
                        s.Sig.EnteredHyperspace.Send(false);
                        // The.ScreenBlock.Show();
                        SOUtils.RemoveSO(so);
                        s.Sys.Universe.Regenerate(so.Seed);
                        // The.ScreenBlock.Hide();
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed regenerating universe after dark portal!");
                    } finally {
                        s.Clock.ForceResume(KL.Clock.ForceStopReason.Cutscene);
                    }
                });
            }
            s.Query.SetQuestBool.Ask(QuestBoolIds.UsedDarkPortal, true);
            The.AchievementTracker.Unlock(AchievementDarkPortal.AchievementId);

            return true;
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
