using Game.Achievements;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Story.Events;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// The Rogue AI ending: reach the Sentinel's home port and push the Intrusion Suite
    /// through it, handing every Sentinel holding in the galaxy to the Virus.
    /// The Hack option rides the existing SOActionQuest bridge, so it only appears on the
    /// registered target and only while this step is active - which is after the Signal
    /// Amplifier is both researched and installed.
    /// See Docs/Architecture/Systems/RogueAIRoute.md.
    /// </summary>
    public sealed class HackSentinelPortQuestStep : BaseQuestStep, IQuestStepWithSOAction {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HackSentinelPortQuestStep(s, c, l));
        }
        public const string CheckId = "HackSentinelPort";
        public override bool SaveLoadTexts => true;
        private readonly QuestStepTarget target;

        public HackSentinelPortQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (isLoading && step.Data.IsEnded) { return; }
            var variable = GetQuestVarByKey(PropertyIdH.Var_Target);
            if (variable is IQuestStepTargetVar targetVar) {
                target = targetVar.GetTargetFor(base.step);
            } else {
                D.Throw("HackSentinelPortQuestStep requires IQuestStepTargetVar, but {0} is not that! {1}",
                    variable, this);
            }
            if (target.Target == null) {
                if (step.IsActive) {
                    FailStep();
                }
                return;
            }
            SetTexts(step, isLoading);
            if (isLoading && step.IsActive) {
                RegisterTarget(true);
            }
        }

        private void SetTexts(QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            step.Title = T.QuestStepHackSentinelPortTitle_V1.F(target.Target.StarmapLink);
            var suite = The.Defs.FastGet(DefIdsH.ObjectsDevicesIntrusionSuite);
            var amplifier = The.Defs.FastGet(DefIdsH.ObjUpgradesSignalAmplifierUpgrade);
            SetDesc(T.QuestStepHackSentinelPortDesc_V2.F(
                amplifier.CodexLink, suite.CodexLink));
        }

        private void RegisterTarget(bool add) {
            if (target?.Target == null) {
                if (add) {
                    D.Throw("Trying to register a null target {0}", this);
                }
                return;
            }
            if (target.IsRegistered == add) { return; }
            if (!S.Query.RegisterQuestStepTarget.Ask(target, add)) {
                D.Throw("Failed to register({2}) quest step target: {0} | {1}",
                    target, this, add);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (target?.Target == null) {
                    // The Sentinel HQ is gone (nuked, or lost to a universe regeneration) -
                    // there is nothing left to break into.
                    FailStep();
                    return;
                }
                RegisterTarget(true);
            }
        }

        public override void Cleanup() {
            RegisterTarget(false);
        }

        public bool HasSOActionFor(SpaceObject so) {
            return so == target?.Target;
        }

        public bool TryAddSOActionFor(SpaceObject so, QuickChoice qc) {
            if (so != target?.Target) {
                D.Err("Trying to add the Hack SO action for the wrong SO! {0}. Target: {1} | {2}",
                    so, target?.Target, this);
                return false;
            }
            qc.AddIconButton(IconId.CRobotEye, T.VerbHack, OnHack);
            return true;
        }

        private void OnHack() {
            // Same shape as ShipHailAction_Hack's suite check: the option is offered once
            // the player can reach the target, and hardware is validated on click.
            if (IntrusionSuiteComp.Find(S) == null) {
                S.Sig.ShowErrorMessage.Send(
                    $"{T.OperationFailed}: {IntrusionSuiteComp.NotAvailable}");
                return;
            }
            if (!IntrusionSuiteComp.HasSignalAmplifier(S)) {
                S.Sig.ShowErrorMessage.Send(
                    $"{T.OperationFailed}: {T.SignalAmplifierNotInstalled}");
                return;
            }
            var converted = RogueAIUtils.EliminateSentinel(S);
            S.Sys.Log.AddLine(Texts.Red(T.SentinelEliminatedTitle));
            // Completes the quest (and sets ReachedEndGame) before the ending plays, so a
            // save taken after the credits already reflects a finished run.
            CompleteStep();
            The.AchievementTracker.Unlock(AchievementSentinelEliminated.AchievementId);
            new RogueAIEndingCutscene(S, converted);
        }
    }
}
