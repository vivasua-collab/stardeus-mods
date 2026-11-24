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
using KL.Pool;

namespace Game.Expeditions {
    public sealed class ExpeditionStageLandingOnBase : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.LandingOnBase, () => {
                return new ExpeditionStageLandingOnBase();
            });
        }
        public override int StageType => ExpeditionStageTypeH.LandingOnBase;
        public override bool CanCallBack(Expedition exp) {
            return false;
        }
        public override bool IsReturning => true;
        public override string Title => T.ExpStageLanding_V1.F(
                The.R.State.Universe.Player.DisplayName);

        public override string MiniWidgetStatusIcon => IconId.CPropulsion;
        // MiniWidgetText is set on first Tick (requires the live player name)

        private Obj flyingShuttle;

        private void CreateFlyingShuttle(Expedition exp) {
            if (exp.Shuttle == null) {
                D.Err("CreateFlyingShuttle has null exp.Shuttle! {0}", exp);
                return;
            }
            if (flyingShuttle == null) {
                flyingShuttle = exp.Shuttle.FlyingShuttle?.Obj;
            }
            if (flyingShuttle != null) { return; }
            var shuttleId = exp.StageData.GetInt("Landing.FlyingShuttleId", 0);
            // We already had a shuttle, possibly before saving
            if (shuttleId != 0) { return; }
            if (exp.Shuttle.IsOut) {
                flyingShuttle = exp.Shuttle.LaunchShuttle(false);
                exp.StageData.SetInt("Landing.FlyingShuttleId", flyingShuttle.Id);
                // Hide the visual
                if (exp.Shuttle.SpaceObject?.Visual != null) {
                    PrefabPool.Despawn(exp.Shuttle.SpaceObject.Visual.gameObject);
                    exp.Shuttle.SpaceObject.Visual = null;
                }
            } else {
                D.Err("Landing phase shuttle marked as not out while not launched anything: {0}",
                    exp.Shuttle.Entity);
            }
            //The.ScreenBlock.SetCenterText(T.ExpeditionReturnedFrom_V1.F(exp.Target.DisplayName), 0.5f);
        }

        private void PutShuttleOnBase(Expedition exp) {
            // Hide the shuttle in our tilemap (show landing pad)
            var so = exp.Shuttle.SpaceObject;
            if (so == null) {
                D.Err("There was no SO on shuttle! {0}", exp.Shuttle);
            } else {
                SOUtils.RemoveSO(so);
                exp.Shuttle.SetSO(null);
            }
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (MiniWidgetText.Value == null) {
                MiniWidgetText.Set(T.VerbLanding);
            }
            CreateFlyingShuttle(exp);

            if (exp.QuestObserver == null) {
                var target = exp.InitialTarget ?? exp.Target;
                var questTargets = exp.S.Query.GetQuestStepTargets.Ask(target);
                exp.QuestObserver = questTargets.First?.ExpeditionObserver;
            }

            if (flyingShuttle != null && flyingShuttle.IsActive) {
                return this;
            }
            exp.StageData.SetInt("Landing.FlyingShuttleId", 0);

            // 2. hide the shuttle object from the map
            PutShuttleOnBase(exp);

            if (exp.IsUnloading) {
                return ExpeditionStages.Find(ExpeditionStageTypeH.Unload);
            } else {
                return ExpeditionStages.Find(ExpeditionStageTypeH.Done);
            }
        }
    }
}
