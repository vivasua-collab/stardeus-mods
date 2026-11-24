using System;
using Game.Constants;
using Game.Data;
using Game.Components;
using KL.Utils;
using UnityEngine;
using Game.Utils;
using Game.CodeGen;
using System.Collections;
using Game.Data.Space;
using KL.Randomness;

namespace Game.Expeditions {
    public sealed class ExpeditionStageTakeoff : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.Takeoff, () => {
                return new ExpeditionStageTakeoff();
            });
        }
        public override int StageType => ExpeditionStageTypeH.Takeoff;
        public override string Title => T.ExpStageTakeoff;

        public override string MiniWidgetStatusIcon => IconId.CPropulsion;

        public ExpeditionStageTakeoff() {
            MiniWidgetText.Set(T.VerbFlying);
        }

        private Obj flyingShuttle;
        public override bool CanCallBack(Expedition exp) {
            return false;
        }

        private void CreateFlyingShuttle(Expedition exp) {
            if (flyingShuttle == null) {
                flyingShuttle = exp.Shuttle.FlyingShuttle?.Obj;
            }
            if (flyingShuttle != null) { return; }
            var shuttleId = exp.StageData.GetInt("Takeoff.FlyingShuttleId", 0);
            if (shuttleId != 0) { return; }

            if (!exp.Shuttle.IsOut) {
                // just in case raiders have collected something
                exp.Shuttle.Tile.MaterialStorage.DumpContents();
                flyingShuttle = exp.Shuttle.LaunchShuttle(true);
                exp.StageData.SetInt("Takeoff.FlyingShuttleId", flyingShuttle.Id);
            } else {
                D.Err("Trying to create flying shuttle for expedition takeoff while shuttle already marked as out: {0}",
                    exp.Shuttle);
            }
        }

        private bool PutShuttleOnStarmap(Expedition exp) {
            // Hide the shuttle in our tilemap (show landing pad)
            var player = exp.S.Universe.Player;
            var shuttleSO = exp.S.Universe.CreateObject();
            shuttleSO.Type = SpaceObjectType.Find(SpaceObjectTypeId.Shuttle);
            shuttleSO.Location = player.Location
                + exp.S.Rng.InsideUnitCircle().normalized
                    * exp.S.Rng.Range(0.01f, 0.02f);
            shuttleSO.Name = exp.Shuttle.Name;
            shuttleSO.OnNameChange = (n) => exp.Shuttle.NameB.Set(n);
            exp.Shuttle.SetSO(shuttleSO);
            var parent = exp.Target?.Parent;
            if (parent == null) {
                parent = player.Parent;
            }
            if (parent.Layer != UniverseLayer.Sector) {
                D.Err("Trying to put shuttle in the wrong universe layer! {0} | {1}",
                    parent, exp);
                return false;
            }
            parent.AddChild(shuttleSO);
            exp.S.Sig.SpaceObjectAdded.Send(shuttleSO);
            return true;
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (exp.S.Universe.Player.Parent != exp.Target.Parent) {
                The.Dialog.ShowSimple(IconId.CComputing,
                    T.AbortingExpeditionTargetTooFar_V1.F(exp.Target.DisplayName));
                exp.Shuttle.KillDustFX();
                exp.MiningOrders.IsAutoUnload = false;
                exp.IsAborted = true;
                return null;
            }

            // We will keep doing that until shuttle is OOB
            CreateFlyingShuttle(exp);
            if (flyingShuttle != null && flyingShuttle.IsActive) { return this; }
            exp.StageData.SetInt("Takeoff.FlyingShuttleId", 0);

            if (PutShuttleOnStarmap(exp)) {
                return ExpeditionStages.Find(ExpeditionStageTypeH.TravelToDest);
            } else {
                return ExpeditionStages.Find(ExpeditionStageTypeH.LandingOnBase);
            }

        }

        public override void Abort(Expedition exp) {
            exp.Shuttle.KillDustFX();
        }
    }
}
