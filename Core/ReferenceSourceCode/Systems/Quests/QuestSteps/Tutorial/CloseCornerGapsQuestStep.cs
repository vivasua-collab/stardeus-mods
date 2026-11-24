using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Systems.Atmo;
using Game.Utils;

namespace Game.Systems.Quests {
    // The second half of enclosing a room (see AreaSealing): walls that meet only at a corner
    // still enclose it, but air leaks out through that corner. Players who never leave such a
    // gap never see this step - it completes hidden the moment it activates.
    public sealed class CloseCornerGapsQuestStep : DeviceRoomQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new CloseCornerGapsQuestStep(s, c, l));
        }
        public const string StepId = "CloseCornerGaps";
        // Enough to find them from the tooltip; the Rooms overlay marks every one
        private const int MaxGapLinks = 5;

        private readonly HashSet<int> gaps = new();
        // Per room, so two devices in one room both see its gaps
        private readonly HashSet<int> roomGaps = new();
        private readonly StringBuilder sb = new();
        private int shownCount = -1;
        private int shownChecksum = -1;

        public CloseCornerGapsQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            if (def == null) { return; }
            RefreshTitle();
            RefreshDesc(0);
        }

        protected override string BuildTitle() {
            shownCount = gaps.Count;
            return T.QuestStepCloseCornerGaps_V2.F(QuestStepLinks.DeviceLink(S, def), gaps.Count);
        }

        protected override void OnActivated() {
            if (CheckCompleted()) {
                step.SetHidden();
                CompleteStep();
            }
        }

        protected override bool CheckCompleted() {
            gaps.Clear();
            var isDone = base.CheckCompleted();
            RefreshTextsIfChanged();
            return isDone;
        }

        // A re-opened room keeps the step open: enclosing it again is part of the job
        protected override bool IsRoomDone(Entity device) {
            var areas = S.Sys.Areas;
            var areaId = areas.At(device.PosIdx);
            if (areaId <= 0) { return false; }
            roomGaps.Clear();
            AreaSealing.CollectCornerGaps(S, areas.AreaById(areaId), roomGaps);
            foreach (var p in roomGaps) {
                gaps.Add(p);
            }
            return roomGaps.Count == 0;
        }

        private void RefreshTextsIfChanged() {
            if (gaps.Count != shownCount) {
                RefreshTitle();
            }
            // Order independent, as set iteration order is not stable
            int checksum = 0;
            unchecked {
                foreach (var p in gaps) {
                    checksum += p * 486187739 + 1;
                }
            }
            if (checksum != shownChecksum) {
                RefreshDesc(checksum);
            }
        }

        private void RefreshDesc(int checksum) {
            shownChecksum = checksum;
            sb.Clear();
            int links = 0;
            foreach (var p in gaps) {
                if (links > 0) {
                    sb.Append(", ");
                }
                if (links == MaxGapLinks) {
                    sb.Append("...");
                    break;
                }
                sb.Append(Texts.LinkToPos(p));
                links++;
            }
            if (links == 0) {
                sb.Append('-');
            }
            step.Desc = T.QuestStepCloseCornerGapsTip_V2.F(sb.ToString(),
                Texts.LinkToOverlay(OverlayId.Areas));
        }
    }
}
