using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Ships;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class DeliverMaterialShipQuestStep : BaseQuestStep, IExchangeWithShipQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new DeliverMaterialShipQuestStep(s, c, l));
        }
        public override bool SaveLoadTexts => true;

        public const string CheckId = "DeliverMaterialShip";
        private MatType matType;
        private int amount;
        private readonly NPCShip target;
        public DeliverMaterialShipQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var amountKey = step.Def.Config.GetString(PropertyIdH.DVar_Amount);
            amount = step.Data.Data.GetInt(amountKey);
            var matTypeIdKey = step.Def.Config.GetString(PropertyIdH.DVar_MatTypeId);
            var matTypeId = step.Data.Data.GetString(matTypeIdKey);
            if (step.Data.Provider is NPCShip ship) {
                target = ship;
            } else {
                D.Throw("Provider is not NPCShip: {0} | {1}", step.Data.Provider, this);
            }
            matType = MatType.Get(matTypeId);
            if (matType == null) {
                throw new Exception($"DeliverMaterial quest step didn't find mat by type id {matTypeId}! {this}");
            }
            SetTexts(s, step, isLoading);
        }

        private void SetTexts(GameState s, QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            var path = target.NameLinked;
            var titleKey = step.Def.GetTitleKey(step.Data);
            var matText = $"{matType.Def.CodexLink} x{amount}";
            if (titleKey != null) {
                step.Title = titleKey.T();
            } else {
                step.Title = T.QuestStepDeliverItemTitle_V1.F(matText);
            }
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T(matText, path);
            } else {
                step.Desc = T.QuestStepDeliverItemDesc_V2.F(matText, path);
            }
        }

        public bool CanExchangeWithShip(NPCShip ship, out string icon, out string title) {
            if (ship != target) {
                icon = null;
                title = null;
                return false;
            }
            icon = matType.IconId;
            title = $"{T.VerbDeliver}: {matType.NameT} x{amount}";
            return true;
        }

        public bool AddQuestPackageOptions(NPCShip ship, GiftChoiceUI ui, GiftDirection direction) {
            if (ship != target || direction != GiftDirection.PlayerToNPC) {
                return false;
            }
            var pack = GiftPackage.ForQuest(direction, ship, matType.IconId, $"{matType.NameT} x{amount}");
            var item = new GiftItem();
            item.Direction = direction;
            item.Type = GiftItemType.Mat;
            item.Mat = new Mat() {
                Type = matType,
                StackSize = amount,
                MaxStackSize = amount,
            };
            pack.AddItem(S, item);

            const float attChange = 0.05f;
            var facText = T.RelationshipWith_V1.F(Texts.Accented(ship.Faction.NameWithGlyph));
            var acceptText = $"{facText}: {Units.PercentageSigned(attChange, 2, true)}";
            pack.WithAccept((p) => {
                step.SetState(QuestStepState.Completed);
                ship.ChangeAttitudeTowards(FactionDef.Player, attChange);
            }, GiftPackage.NewConsequence(IconId.CCheck, acceptText));

            ui.AddOption(pack);
            return true;
        }
    }
}
