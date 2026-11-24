using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class RespondToSignalQuestStep : BaseQuestStep, IQuestStepWithTarget {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new RespondToSignalQuestStep(s, c, l));
        }
        public const string CheckId = "RespondToSignal";
        public override bool SaveLoadTexts => true;
        private QuestStepTarget target;
        public RespondToSignalQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var variable = GetQuestVarByKey(PropertyIdH.Var_Signal);
            if (variable is IQuestStepTargetVar targetVar) {
                target = targetVar.GetTargetFor(step);
                target.OnInteract = OnRespondToSignal;
            } else {
                throw new Exception($"RespondToSignal requires IQuestStepTargetVar, but {variable} is not that! {this}");
            }
            SetTexts(step, isLoading);
            if (isLoading && step.IsActive) {
                s.Query.RegisterQuestStepTarget.Ask(target, true);
            }
        }

        private void SetTexts(QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            string path = null;
            if (target.Target is SpaceObject so) {
                path = so.Parent.ParentPath(fromPlayer: false);
            } else {
                D.Throw("Target is not space object: {0} | {1}", target.Target, this);
            }
            var sigLink = target.Target.StarmapLink;
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey != null) {
                step.Title = titleKey.T(sigLink, path);
            } else {
                step.Title = T.QuestStepRespondToSignalTitle_V2.F(sigLink, path);
            }
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T(path, sigLink);
            } else {
                step.Desc = T.QuestStepRespondToSignalDesc_V2.F(path, sigLink);
            }
        }

        private void OnRespondToSignal() {
            var msg = step.RandomMsg("MessageKeys", Rng.Unseeded);
            The.Dialog.ShowSimple(IconId.CSignal, msg);
            CompleteStep();
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (target?.IsRegistered == false) {
                    S.Query.RegisterQuestStepTarget.Ask(target, true);
                }
            } else if (after == QuestStepState.Completed || after == QuestStepState.Failed) {
                UnregisterTarget();
            }
        }

        private void UnregisterTarget() {
            if (target?.IsRegistered == true) {
                S.Query.RegisterQuestStepTarget.Ask(target, false);
                target = null;
            }
        }

        public override void OnQuestStateChange(QuestState before, QuestState after) {
            if (after == QuestState.Completed || after == QuestState.Failed) {
                UnregisterTarget();
            }
        }

        public QuestStepTarget GetTarget() {
            return target;
        }
    }
}
