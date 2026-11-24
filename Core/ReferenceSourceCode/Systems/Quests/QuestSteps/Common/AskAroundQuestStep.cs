using System;
using System.Linq;
using Game.CodeGen;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class AskAroundQuestStep : BaseQuestStep, IInteractiveQuestStep {
        public const string StepId = "AskAround";
        private const string Key_Count = "Count";
        private const string Key_Memory = "Memory";
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new AskAroundQuestStep(s, c, l));
        }

        private readonly string hailText;
        private readonly string hailIcon;
        public QuestStep Step => step;
        public readonly QuestDef offerQuest;
        public AskAroundQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey == null) {
                D.Throw("Misconfigured AskAroundQuestStep, no TitleKey! {0}", step.Def);
            }
            var hailTextKey = config.GetString(PropertyIdH.HailTextKey);
            hailText = hailTextKey.TOrNull();
            if (hailText == null) {
                D.Throw("Misconfigured AskAroundQuestStep, HailTextKey not found: {0} | {1}",
                    hailTextKey, this);
            }
            hailIcon = config.GetString(PropertyIdH.HailIcon);
            var oQuestId = config.GetString(PropertyIdH.OfferQuestId, null);
            if (oQuestId != null) {
                offerQuest = QuestDef.Get(oQuestId);
                if (offerQuest == null) {
                    D.Throw("OfferQuestId not found: {0} | {1}", oQuestId, this);
                }
            }
            if (!isLoading) {
                Curve counts = config.GetFloatSet(PropertyIdH.CountCurve);
                var count = step.SeededRng.IntCurve(counts);
                var data = step.Data.GetOrCreateData();
                if (count < 1) {
                    D.Err("Count is < 1 in AskAroundQuestStep! {0}", this);
                    count = 3;
                }
                data.SetInt(DataKey(Key_Count), count);
            }

            step.Title = titleKey.T();
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T();
            }
            if (isLoading && step.IsActive) {
                S.Query.RegisterGlobalInteractiveQuestStep.Ask(this, true);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                S.Query.RegisterGlobalInteractiveQuestStep.Ask(this, true);
            }
        }

        public override void Cleanup() {
            S.Query.RegisterGlobalInteractiveQuestStep.Ask(this, false);
        }
        public bool CanInteractWith(SpaceObject so, out string icon, out string title) {
            icon = hailIcon;
            title = hailText;
            if (so.IsShip && !so.Ship.IsDerelict) {
                var mem = step.Data.Data.GetIntSet(DataKey(Key_Memory), null);
                if (!Arrays.ContainsSafe(mem, (int) so.Ship.Id)) {
                    return true;
                }
            }
            return false;
        }

        public bool InteractWithShip(NPCShip target, ShipHailActionUI ui) {
            // remember the ship for future
            var memKey = DataKey(Key_Memory);
            var mem = step.Data.Data.GetIntSet(memKey, null);
            if (mem == null) {
                mem = new int[1];
            } else {
                if (Arrays.Contains(mem, (int) target.Id)) {
                    D.Err("memory already contains ship! {0} | {1}", target, step);
                    return false;
                }
                Array.Resize(ref mem, mem.Length + 1);
            }
            mem[^1] = (int) target.Id;
            step.Data.Data.SetIntSet(memKey, mem);

            var cntKey = DataKey(Key_Count);
            var count = step.Data.Data.GetInt(cntKey, 0);
            if (count > 0) {
                count--;
                step.Data.Data.SetInt(cntKey, count);
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.Generic,
                    ShipChatResponseType.Negative);
            } else {
                if (offerQuest != null) {
                    SpaceObject source = null;
                    if (config.GetBool(PropertyIdH.TransferQuestSource, false)) {
                        source = target.SO;
                    }
                    var qd = QuestData.CreateFor(S, offerQuest, step.SeededRng.Fork(), source);
                    var q = S.Query.AddQuest.Ask(qd,
                        config.GetBool(PropertyIdH.AutoActivateQuest, true));
                    if (q == null) {
                        D.Err("Failed creating offered quest: {0} | {1}", qd, this);
                        FailStep();
                        return false;
                    }
                    if (source != null) {
                        source.QuestId = qd.Id;
                    }
                    A.S.UIQuery.ShowQuest.Ask(q);
                    // A.S.Sig.ShowContextMenu.Send(q.UI);
                }
                ui.ShowHailPanel();
                CompleteStep();
            }
            return true;
        }
    }
}
