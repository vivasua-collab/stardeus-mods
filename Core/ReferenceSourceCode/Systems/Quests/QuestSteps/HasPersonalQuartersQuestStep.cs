using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Sleep;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Completes when the giver colonist has personal quarters by the one shared rule,
    /// AreaSys.PersonalQuartersAreaOf. Reads the colonist from step.Data.Provider
    /// (must be a ColonistQuestProvider).
    /// </summary>
    public sealed class HasPersonalQuartersQuestStep : BaseQuestStep,
            ITickListener, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HasPersonalQuartersQuestStep(s, c, l));
        }

        public const string CheckId = "HasPersonalQuarters";

        private readonly BeingData colonist;
        private readonly string baseDesc;
        private bool isSubscribed;
        private bool needsCheckOnTick;

        public HasPersonalQuartersQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (step.Data.Provider is ColonistQuestProvider cp) {
                colonist = cp.BeingData;
            }
            if (colonist == null) {
                D.Throw("HasPersonalQuarters: quest provider is not a ColonistQuestProvider | {0}", step);
            }

            var key = step.Def.GetTitleKey(step.Data);
            if (key != null) {
                step.Title = key.T(colonist.Mind.LinkToShortName);
            } else {
                step.Title = T.QuestStepHasPersonalQuartersOne_V1.F(colonist.Mind.LinkToShortName);
            }
            AreaSys.PersonalQuartersSizeRange(colonist, out var min, out var max);
            baseDesc = T.QuestStepHasPersonalQuartersDesc2_V2.F(min, max);
            step.Desc = baseDesc;

            if (isLoading && step.IsActive) {
                needsCheckOnTick = true;
                Subscribe();
            }
        }

        public void Tick(long ticks) {
            if (needsCheckOnTick && Ready.Energy) {
                needsCheckOnTick = false;
                if (step.IsActive && CheckCompleted()) {
                    CompleteStep();
                }
            }
        }

        public bool CheckCompleted() {
            if (colonist == null || colonist.Being == null || !colonist.Being.IsAlive) {
                return false;
            }
            var hasQuarters = S.Sys.Areas.HasPersonalQuarters(colonist);
            var reason = hasQuarters ? null : RoomSizeReason();
            step.Desc = reason == null ? baseDesc : $"{baseDesc}{GlyphMap.BRHR}{reason}";
            return hasQuarters;
        }

        // The size range is in the description, but nothing else tells the player what the
        // room they built measures (STA-1771). Only reasons with a text of their own.
        private string RoomSizeReason() {
            var areas = S.Sys.Areas;
            if (!areas.TryGetOwnBedRoom(colonist, out var roomId)) { return null; }
            if (roomId <= 0) { return T.NotEnclosed; }
            var size = areas.AreaById(roomId).Count;
            AreaSys.PersonalQuartersSizeRange(colonist, out var min, out var max);
            if (size < min) {
                return $"{T.SleepEffRoomTooSmall} {Texts.GreyParentheses(Units.XOutOfY(size, min))}";
            }
            if (size > max) {
                return $"{T.SleepEffRoomTooLarge} {Texts.GreyParentheses(Units.XOutOfY(size, max))}";
            }
            return null;
        }

        private void OnBedSlotStateChanged(uint beingDataId) {
            if (beingDataId != colonist.Id) { return; }
            needsCheckOnTick = true;
        }

        private void OnRegisterBed(BedComp bed, bool registered) {
            needsCheckOnTick = true;
        }

        private void OnConstructionCompleted(Tile tile) {
            needsCheckOnTick = true;
        }

        private void OnEntityRemoved(Entity entity) {
            needsCheckOnTick = true;
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.BedSlotStateChanged.AddListener(OnBedSlotStateChanged);
            S.Sig.RegisterBed.AddListener(OnRegisterBed);
            S.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            S.Sig.EntityRemoved.AddListener(OnEntityRemoved);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                // Activating mid-quest (e.g. via ActivateWhen) only starts listening for
                // future signals - re-check now in case the condition is already met.
                needsCheckOnTick = true;
                Subscribe();
            }
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            S.Sig?.BedSlotStateChanged.RemoveListener(OnBedSlotStateChanged);
            S.Sig?.RegisterBed.RemoveListener(OnRegisterBed);
            S.Sig?.ConstructionCompleted.RemoveListener(OnConstructionCompleted);
            S.Sig?.EntityRemoved.RemoveListener(OnEntityRemoved);
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
