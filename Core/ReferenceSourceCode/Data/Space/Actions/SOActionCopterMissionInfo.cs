using Game.CodeGen;
using Game.Constants;
using Game.Systems.Copters;
using Game.Systems.UIUX;
using KL.Utils;
using Game.Utils;
using Game.Input;

namespace Game.Data.Space {
    public sealed class SOActionCopterMissionInfo : SOAction, ITickListener {
        public const string ActionId = "CopterMissionInfo";

        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionCopterMissionInfo());
        }

        private B<string> textB;

        protected override void OnFlush() {
            textB = null;
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (!ctx.S.BB.Get(BBKeyH.MiningAuto, false) && !A.InstantBuild) {
                return false;
            }
            return ctx.S.Sys.Copters?.TryGetMissionFor(ctx.SO, out _) == true;
        }

        public override bool AddOptions(QuickChoice qc) {
            var dynText = new QuickChoice.DynamicText() {
                TextB = textB = new B<string>(string.Empty),
                TipFunc = ShowMissionTooltip,
            };
            Tick(0);
            qc.AddDynamicText(textB.Value, dynText);
            return true;
        }

        public void Tick(long currentTick) {
            if (textB == null) {
                return;
            }
            if (ctx.S.Sys.Copters.TryGetMissionFor(ctx.SO, out var mission)) {
                UpdateMission(mission);
            } else {
                var text = $"{T.ObjectsDevicesCargocopter} {GlyphMap.ArrowRight} {T.AdjDone}";
                textB.Set(text);
            }
        }

        private void UpdateMission(CopterMissionData mission) {
            var text = CopterMissionUtils.MissionSummaryFor(ctx.S, mission);
            text = $"{T.ObjectsDevicesCargocopter} {GlyphMap.ArrowRight} {text}";
            textB.Set(text);
        }

        private string ShowMissionTooltip() {
            if (!ctx.S.Sys.Copters.TryGetMissionFor(ctx.SO, out var mission)) {
                return null;
            }
            return CopterMissionUtils.MissionTooltipFor(ctx.S, mission);
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
