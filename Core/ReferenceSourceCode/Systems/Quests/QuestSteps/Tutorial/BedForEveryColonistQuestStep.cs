using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Sleep;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    // The tutorial version of HasBedAssigned: every colonist on the ship, not one quest giver.
    // Pets are left out on purpose - a pet bed is a different lesson, and a colonist sleeping
    // on the floor is the problem this quest exists for.
    public sealed class BedForEveryColonistQuestStep : BaseQuestStep,
            ITickListener, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new BedForEveryColonistQuestStep(s, c, l));
        }

        public const string StepId = "BedForEveryColonist";
        private bool isSubscribed;
        private bool needsCheck = true;
        private int lastWithBed = -1;
        private int lastTotal = -1;

        public BedForEveryColonistQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            step.Desc = T.QuestStepBedForEveryColonistTip_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsFurnitureBedWood),
                Texts.LinkToCodex(DefIdsH.ObjectsFurnitureBedSteel));
            // On load the bed queries are not answered yet, the first tick counts instead
            if (!isLoading) {
                Count(out var withBed, out var total);
                UpdateTitle(withBed, total);
            }
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public void Tick(long ticks) {
            // Colonists wake up, die and leave without a bed signal, so a slow recount
            // catches those, while bed changes re-check right away. A game minute is one
            // tick, so the recount ran on every tick.
            if (needsCheck || (step.IsActive && ticks % Consts.TicksPer15Minutes == 0)) {
                needsCheck = false;
                if (CheckCompleted() && step.IsActive) {
                    CompleteStep();
                }
            }
        }

        public bool CheckCompleted() {
            Count(out var withBed, out var total);
            UpdateTitle(withBed, total);
            return total > 0 && withBed == total;
        }

        // A colonist counts once they own a bed, or once a free bed is waiting for them: beds
        // are only claimed when somebody gets tired, so a ship that starts with beds would
        // otherwise show this quest for nothing. Free slots are shared out one per colonist.
        private void Count(out int withBed, out int total) {
            withBed = 0;
            total = 0;
            if (S?.Beings?.Alive == null) { return; }
            int unassigned = 0;
            BeingData anyUnassigned = null;
            foreach (var being in S.Beings.Alive) {
                var data = being.Data;
                if (!NeedsBed(data)) { continue; }
                total++;
                if (HasPermanentBed(data)) {
                    withBed++;
                } else {
                    unassigned++;
                    anyUnassigned = data;
                }
            }
            if (unassigned > 0) {
                withBed += System.Math.Min(unassigned, FreePermanentSlots(anyUnassigned));
            }
            // The quest is offered before the first colonist wakes up, and the only colonist
            // can die, so an empty ship still asks for one bed: 0/0 read as a broken quest,
            // and a bed built now is what the next colonist out of stasis needs anyway
            if (total == 0) {
                total = 1;
                withBed = FreePermanentSlots(null) > 0 ? 1 : 0;
            }
        }

        private static readonly BedComp[] bedBuffer = new BedComp[512];

        // A null sleeper stands for the next human out of stasis
        private int FreePermanentSlots(BeingData sleeper) {
            int found = S.Query.FindBeds.Ask(bedBuffer, sleeper, false);
            int free = 0;
            for (int i = 0; i < found; i++) {
                var bed = bedBuffer[i];
                bedBuffer[i] = null;
                if (!bed.Tile.IsConstructed) { continue; }
                foreach (var slot in bed.Slots) {
                    if (slot.IsTemporary || slot.OwnerDataId != 0) { continue; }
                    if (sleeper == null ? IsHumanSlot(slot) : slot.IsCompatibleWith(sleeper)) {
                        free++;
                    }
                }
            }
            return free;
        }

        private static bool IsHumanSlot(BedSlot slot) {
            var flags = Species.Get(SpeciesIdH.Human).StatFlags;
            return (flags & slot.Compatibility) == slot.Compatibility;
        }

        private static bool NeedsBed(BeingData data) {
            if (data == null || !data.IsPlayer || data.IsGuest) { return false; }
            if (data.Being == null || !data.Being.IsAlive) { return false; }
            if (!data.Being.Species.IsHumanoid) { return false; }
            return data.HasNeed(NeedIdH.Sleep);
        }

        private bool HasPermanentBed(BeingData data) {
            var bed = S.Query.FindBed.Ask(data, false);
            if (bed == null) { return false; }
            var slot = bed.SlotFor(data);
            return slot != null && !slot.IsTemporary;
        }

        private void UpdateTitle(int withBed, int total) {
            if (withBed == lastWithBed && total == lastTotal) { return; }
            lastWithBed = withBed;
            lastTotal = total;
            step.Title = T.QuestStepBedForEveryColonist_V2.F(withBed, total);
        }

        private void OnBedSlotStateChanged(uint beingDataId) {
            needsCheck = true;
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.BedSlotStateChanged.AddListener(OnBedSlotStateChanged);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                needsCheck = true;
                Subscribe();
            }
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            S.Sig?.BedSlotStateChanged.RemoveListener(OnBedSlotStateChanged);
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
