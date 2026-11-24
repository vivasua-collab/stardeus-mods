using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Expeditions;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Sends the delivery quest passenger out on a shuttle to a POI. The passenger (plus at
    /// least one escort) is locked into the crew via the registered target's RequiredCrewMindId.
    /// On arrival the passenger leaves the colony (despawned, bed unassigned) and a reward
    /// package is spawned aboard the returning shuttle. Modeled on ExplorePOIQuestStep.
    /// </summary>
    public sealed class DeliverPassengerToPOIQuestStep : BaseQuestStep, IQuestExpeditionHandler {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new DeliverPassengerToPOIQuestStep(s, c, l));
        }

        public const string CheckId = "DeliverPassengerToPOI";
        public const string Key_Delay = "Delay";
        public override bool SaveLoadTexts => true;
        public bool IsExpeditionStageCancellable => true;

        private QuestStepTarget target;
        private QuestPackageFlags flags;
        private QuestDef packageQuest;
        private int actionDelayTicks;

        public DeliverPassengerToPOIQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var strFlags = config.GetStringSet(PropertyIdH.PackageFlags, null);
            flags = strFlags != null
                ? Enums.FlagsFromStringArray_Int<QuestPackageFlags>(strFlags, this)
                : QuestPackageFlags.Default;
            packageQuest = QuestDef.Get(
                config.GetString(PropertyIdH.PackageQuestId, null), required: false, errorCtx: this);

            var poiVar = config.GetString(PropertyIdH.Var_POI, "POI");
            if (!step.Data.Variables.TryGetValue(Hashes.S(poiVar), out var variable)) {
                throw new Exception($"Variable {poiVar} not found for quest check: {step} | {this}");
            }
            if (variable is IQuestStepTargetVar targetVar) {
                target = targetVar.GetTargetFor(step);
                target.ExpeditionHandler = this;
            } else {
                throw new Exception(
                    $"DeliverPassengerToPOI requires IQuestStepTargetVar, but {variable} is not that! {this}");
            }

            SetTexts(step, isLoading);
            if (isLoading) {
                if (step.IsActive) { RegisterTarget(); }
                actionDelayTicks = step.Data.Data?.GetInt(step.Def.DataKey(Key_Delay)) ?? 0;
            }
            if (actionDelayTicks == 0) {
                var hoursFrom = config.GetFloat(PropertyIdH.ExpeditionMinHours, 3f);
                var hoursTo = config.GetFloat(PropertyIdH.ExpeditionMaxHours, 6f);
                var hours = step.SeededRng.Range(hoursFrom, hoursTo);
                actionDelayTicks = (int) (hours * Consts.TicksPerHour);
                step.Data.GetOrCreateData().SetInt(step.Def.DataKey(Key_Delay), actionDelayTicks);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RegisterTarget();
            }
        }

        private void RegisterTarget() {
            if (target == null) { return; }
            // Lock the passenger into the crew of any expedition to this POI.
            target.RequiredCrewMindId = PassengerMindId();
            if (!target.IsRegistered) {
                S.Query.RegisterQuestStepTarget.Ask(target, true);
            }
        }

        private uint PassengerMindId() {
            return (uint) (step.Data.Data?.GetInt(PassengerUtils.PassengerMindIdKey, 0) ?? 0);
        }

        public IExpeditionStage RunExpedition(Expedition exp,
                ExpeditionStageQuest stage, long ticks) {
            var durTicks = ticks - exp.StageStartTick;
            if (durTicks < actionDelayTicks) {
                // still travelling / dropping off
                return stage;
            }

            var passenger = S.Query.FindMind.Ask(PassengerMindId())?.Owner;
            bool hasPassenger = passenger != null && exp.Crew.Contains(passenger);
            if (!hasPassenger || exp.Crew.Count < 2) {
                // The passenger must be aboard, accompanied by at least one escort.
                var msg = "quest.passenger.must.be.aboard".TOrNull()
                    ?? "The passenger and an escort must both be aboard the shuttle.";
                The.Dialog.ShowSimple(GraphicsId.ObjContainersQuestPackage, msg);
                return stage.ReturnToBase(S);
            }

            // Passenger leaves the colony at the destination.
            exp.CrewRemove(passenger);
            // CrewRemove only removes from the list; clear the flag so ValidateExpeditionCrew
            // doesn't complain about a being marked Expedition that's no longer in any crew.
            passenger.StateFlags &= ~BeingState.Expedition;
            PassengerUtils.EndGuestRole(S, passenger, despawn: true);

            var foundMsgKey = config.GetString(PropertyIdH.ExpeditionFoundItemMsgKey, null);
            var foundMsg = foundMsgKey != null ? foundMsgKey.T() : T.QuestExpeditionFoundPackage;
            The.Dialog.ShowSimple(GraphicsId.ObjContainersQuestPackage, foundMsg);

            var pack = QuestPackage.CreateFor(step, flags);
            PopulatePackageRewards(pack, packageQuest);
            if (S.Query.AddQuestPackage.Ask(pack)) {
                exp.QuestPackageId = pack.Id;
            }

            CompleteStep();
            stage.RemovePOI(exp);
            return stage.ReturnToBase(S);
        }

        private void SetTexts(QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            string path = null;
            if (target.Target is SpaceObject so) {
                path = so.Parent.ParentPath(fromPlayer: false);
            }
            var poiLink = target.Target?.StarmapLink;
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey != null) {
                step.Title = titleKey.T(poiLink, path);
            }
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T(path, poiLink);
            }
        }

        public override void Cleanup() {
            if (target?.IsRegistered == true) {
                S.Query.RegisterQuestStepTarget.Ask(target, false);
                target = null;
            }
        }
    }
}
