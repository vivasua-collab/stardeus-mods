using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class InteractWithSOQuestStep : BaseQuestStep, IQuestStepWithSOAction {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new InteractWithSOQuestStep(s, c, l));
        }
        public override bool SaveLoadTexts => true;
        public const string CheckId = "InteractWithSO";
        private readonly QuestStepTarget target;
        private readonly QuestDef offerQuest;
        private readonly QuestDef[] offerQuests;
        private readonly string interactMsg;
        public InteractWithSOQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (isLoading && step.Data.IsEnded) { return; }
            interactMsg = GetTextFromVar(PropertyIdH.MessageOnInteract, required: false);
            var isQuestProvider = config.GetBool(PropertyIdH.QuestProvider, false);
            if (isQuestProvider) {
                if (isLoading && step.IsActive) {
                    D.Warn("Loading interact with SO quest provider step: {0}", this);
                }
                var prov = step.Data.Provider;
                if (prov == null) {
                    D.Throw("InteractWithSOQuestStep tried QuestProvider but it was not set! {0}",
                        this);
                }
                target = new QuestStepTarget() {
                    Target = step.Data.Provider,
                    Step = step,
                };
            } else {
                var variable = GetQuestVarByKey(PropertyIdH.Var_Target);
                if (variable is IQuestStepTargetVar targetVar) {
                    target = targetVar.GetTargetFor(base.step);
                } else {
                    D.Throw("ReachRecoveredPlanetCoords requires IQuestStepTargetVar, but {0} is not that! {1}",
                        variable, this);
                }
            }
            if (!IsLive(target.Target)) {
                if (step.IsActive) {
                    FailStep();
                }
                return;
            }
            var offerQuestId = config.GetString(PropertyIdH.OfferQuestId, null);
            if (offerQuestId != null) {
                offerQuest = QuestDef.Get(offerQuestId, required: true, errorCtx: this);
            }
            var offerQuestIds = config.GetStringSet(PropertyIdH.OfferQuestIds, null);
            if (offerQuestIds != null) {
                offerQuests = new QuestDef[offerQuestIds.Length];
                for (int i = 0; i < offerQuestIds.Length; i++) {
                    var q = QuestDef.Get(offerQuestIds[i], required: true, errorCtx: this);
                    offerQuests[i] = q;
                }
            }
            if (!isLoading) {
                var soLink = target.Target.StarmapLink;
                step.Title = step.Def.GetTitleKey(step.Data).T(soLink);
            } else if (step.IsActive) {
                RegisterTarget(true);
            }
        }

        /// <summary>
        /// A plain SpaceObject target (POI/port/signal) is removed from the world via
        /// SOUtils.RemoveSO, which orphans it (Parent = null) without nulling out any
        /// reference other code still holds - e.g. a shared SectorPOI (SharedVar_LoadPOIId)
        /// nuked by an unrelated quest after this step already resolved its target. Ship-backed
        /// providers are excluded: their removal/derelict lifecycle is already handled
        /// separately (QuestSys.OnSORemoved / OnShipTurnedDerelict set the quest's Provider
        /// itself, not a step's Var_Target SO).
        /// </summary>
        private static bool IsLive(IQuestProvider target) {
            return target != null && (target is not SpaceObject so || so.Parent != null);
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
                if (target == null || !IsLive(target.Target)) {
                    // target var resolved to nothing, or its referenced SO was destroyed since -
                    // there's nothing to interact with, so the step can never complete
                    FailStep();
                    return;
                }
                // registers on both fresh ActivateWhen transitions and, redundantly but
                // harmlessly, right after the ctor's own isLoading && step.IsActive registration
                RegisterTarget(true);
            }
        }

        public override void Cleanup() {
            RegisterTarget(false);
        }


        public bool HasSOActionFor(SpaceObject so) {
            if (so.IsShip) {
                return so.Ship == target.Target;
            }
            return so == target.Target;
        }

        public bool TryAddSOActionFor(SpaceObject so, QuickChoice qc) {
            IQuestProvider prov = so;
            if (so.IsShip) {
                prov = so.Ship;
            }

            if (prov != target.Target) {
                D.Err("Trying to get SOActionUDB for wrong SO! {0}. Target: {1} | {2}",
                    so, target.Target, this);
                return false;
            }
            qc.AddIconButton(IconId.CCallOngoing, T.VerbCommunicate, OnInteract);
            return true;
        }

        private bool TryOfferQuest() {
            if (offerQuest == null && offerQuests == null) {
                return true;
            }
            var autoActivate = config.GetBool(PropertyIdH.AutoActivateQuest, true);
            bool added = false;
            if (offerQuest != null) {
                added |= AddQuest(offerQuest, step.SeededRng.Fork(),
                    target.Target, autoActivate, true);
            }
            if (offerQuests != null) {
                foreach (var q in offerQuests) {
                    added |= AddQuest(q, step.SeededRng.Fork(),
                        provider: null, autoActivate, false);
                }
            }
            return added;
        }

        private bool AddQuest(QuestDef def, Rng rng, IQuestProvider provider,
                bool autoActivate, bool showContext) {
            var qData = QuestData.CreateFor(S, def, rng, provider);
            if (qData == null) {
                D.Err("Failed creating QuestData for quest: {0} | {1}",
                    offerQuest, this);
                return false;
            }
            var q = S.Query.AddQuest.Ask(qData, autoActivate);
            if (q != null) {
                if (provider != null) {
                    provider.QuestId = qData.Id;
                }
                if (showContext) {
                    // S.Sig.ShowContextMenu.Send(q.UI);
                    S.UIQuery.ShowQuest.Ask(q);
                }
            } else {
                D.Err("Failed starting Quest for step: {0} | {1}",
                    qData, this);
                return false;
            }
            return true;
        }

        private void OnInteract() {
            // talking to the target goes through the communicator, same as hailing a ship
            if (!S.Sys.Ship.HasCommunicator()) {
                S.Sig.ShowErrorMessage.Send(T.CommunicatorMissing);
                return;
            }
            if (interactMsg != null) {
                The.Dialog.ShowSimple(
                    config.GetString(PropertyIdH.MessageIcon, IconId.CQuest),
                    interactMsg);
            }
            if (TryOfferQuest()) {
                CompleteStep();
            } else {
                FailStep();
            }
        }
    }
}
