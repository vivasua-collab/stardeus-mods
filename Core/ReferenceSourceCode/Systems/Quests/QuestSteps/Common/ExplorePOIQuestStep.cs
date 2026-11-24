using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Expeditions;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ExplorePOIQuestStep : BaseQuestStep, IQuestExpeditionHandler {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ExplorePOIQuestStep(s, c, l));
        }
        public const string CheckId = "ExplorePOI";
        public const string Key_Delay = "Delay";
        public override bool SaveLoadTexts => true;
        private QuestStepTarget target;
        private QuestPackageFlags flags;
        private QuestDef packageQuest;
        public bool IsExpeditionStageCancellable => !isShowingChoice;
        private bool isShowingChoice;
        private readonly int actionDelayTicks;
        public ExplorePOIQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {

            var strFlags = config.GetStringSet(PropertyIdH.PackageFlags, null);
            if (strFlags != null) {
                flags = Enums.FlagsFromStringArray_Int<QuestPackageFlags>(strFlags, this);
            } else {
                flags = QuestPackageFlags.Default;
            }
            var poiVar = config.GetString(PropertyIdH.Var_POI, "POI");
            if (!step.Data.Variables.TryGetValue(Hashes.S(poiVar), out var variable)) {
                throw new Exception($"Variable {poiVar} not found for quest check: {step} | {this}");
            }
            packageQuest = QuestDef.Get(
                config.GetString(PropertyIdH.PackageQuestId, null),
                    required: false, errorCtx: this);
            if (variable is IQuestStepTargetVar targetVar) {
                target = targetVar.GetTargetFor(step);
                target.ExpeditionHandler = this;
            } else {
                throw new Exception(
                    $"ExplorePOI requires IQuestStepTargetVar, but {variable} is not that! {this}");
            }
            SetTexts(step, isLoading);
            if (isLoading) {
                if (step.IsActive) {
                    RegisterTarget();
                }
                actionDelayTicks = step.Data.Data?.GetInt(step.Def.DataKey(Key_Delay)) ?? 0;
            }
            if (actionDelayTicks == 0) {
                var hoursFrom = config.GetFloat(PropertyIdH.ExpeditionMinHours, 3f);
                var hoursTo = config.GetFloat(PropertyIdH.ExpeditionMaxHours, 6f);
                var hours = step.SeededRng.Range(hoursFrom, hoursTo);
                actionDelayTicks = (int) (hours * Consts.TicksPerHour);
                var qData = step.Data.GetOrCreateData();
                qData.SetInt(step.Def.DataKey(Key_Delay), actionDelayTicks);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RegisterTarget();
            }
        }

        private void RegisterTarget() {
            if (target?.IsRegistered == false) {
                S.Query.RegisterQuestStepTarget.Ask(target, true);
            }
        }

        private void SetTexts(QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            string path = null;
            if (target.Target is SpaceObject so) {
                path = so.Parent.ParentPath(fromPlayer: false);
            } else {
                D.Throw("Target not SO: {0} | {1}", target.Target, this);
            }
            var poiLink = target.Target.StarmapLink;
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey != null) {
                step.Title = titleKey.T(poiLink, path);
            } else {
                step.Title = T.QuestStepExplorePoiTitle_V2.F(poiLink, path);
            }
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T(path, poiLink);
            } else {
                step.Desc = T.QuestStepExplorePoiDesc_V2.F(path, poiLink);
            }
        }

        public IExpeditionStage RunExpedition(Expedition exp,
                ExpeditionStageQuest stage, long ticks) {
            var colonistProvider = step.Data.Provider as ColonistQuestProvider;
            if (colonistProvider != null
                    && !CrewContainsMind(exp, colonistProvider.Mind.Id)) {
                var msg1 = T.ExpeditionFailMissingCrew_V2.F(colonistProvider.BeingData.Name,
                    exp.Target?.StarmapLink ?? T.Unknown);
                The.Dialog.ShowSimple(GraphicsId.ObjContainersQuestPackage, msg1);
                return stage.ReturnToBase(S);
            }

            var durTicks = ticks - exp.StageStartTick;
            if (durTicks < actionDelayTicks) {
                // still exploring
                return stage;
            }

            var msgKey = step.Def.Config.GetString(PropertyIdH.ExpeditionFoundItemMsgKey, null);
            string msg;
            if (msgKey != null) {
                msg = msgKey.T();
            } else {
                msg = T.QuestExpeditionFoundPackage;
            }
            The.Dialog.ShowSimple(GraphicsId.ObjContainersQuestPackage, msg);

            var pack = QuestPackage.CreateFor(step, flags);

            PopulatePackageRewards(pack, packageQuest);
            if (S.Query.AddQuestPackage.Ask(pack)) {
                exp.QuestPackageId = pack.Id;
            }

            CompleteStep();
            stage.RemovePOI(exp);
            return stage.ReturnToBase(S);
        }

        private static bool CrewContainsMind(Expedition exp, uint mindId) {
            for (int i = exp.Crew.Count - 1; i >= 0; i--) {
                if (exp.Crew[i].Mind.Id == mindId) { return true; }
            }
            return false;
        }

        public override void Cleanup() {
            if (target?.IsRegistered == true) {
                S.Query.RegisterQuestStepTarget.Ask(target, false);
                target = null;
            }
        }
    }
}
