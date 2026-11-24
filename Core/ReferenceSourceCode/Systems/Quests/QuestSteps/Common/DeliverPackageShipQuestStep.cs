using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Ships;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class DeliverPackageShipQuestStep : BaseQuestStep, IExchangeWithShipQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new DeliverPackageShipQuestStep(s, c, l));
        }
        public override bool SaveLoadTexts => true;

        public const string CheckId = "DeliverPackageShip";
        private IQuestProvider provider;
        public DeliverPackageShipQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            provider = step.Data.Provider;
            SetTexts(s, step, isLoading);
        }

        private void SetTexts(GameState s, QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            var path = provider.StarmapLink;
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey != null) {
                step.Title = titleKey.T();
            } else {
                step.Title = T.QuestStepDeliverPackageTitle;
            }
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T(path);
            } else {
                step.Desc = T.QuestStepDeliverPackageDesc_V1.F(path);
            }
        }

        public bool CanExchangeWithShip(NPCShip ship, out string icon, out string title) {
            if (ship != provider && ship.SO != provider) {
                icon = null;
                title = null;
                return false;
            }
            icon = GraphicsId.ObjContainersQuestPackage;
            title = T.QuestStepDeliverPackageTitle;
            return true;
        }

        public bool AddQuestPackageOptions(NPCShip ship, GiftChoiceUI ui, GiftDirection direction) {
            if ((ship != provider && ship.SO != provider) || direction != GiftDirection.PlayerToNPC) {
                return false;
            }
            var def = The.Defs.FastGet(DefIdsH.ObjContainersQuestPackage);
            var giftPackage = GiftPackage.ForQuest(direction, ship, def.Preview, def.NameT);

            var packages = new List<QuestPackage>();
            var givenPackages = new List<QuestPackage>();
            var packCnt = S.Query.GetQuestPackages.Ask(step.Data, packages);
            if (packCnt > 0) {
                for (int i = packages.Count - 1; i >= 0; i--) {
                    QuestPackage pack = packages[i];
                    if (pack.State == QuestPackageState.Spawned) {
                        var packComp = S.Components.FindFirstMatching<QuestPackageComp>(
                            p => p.QuestPackageId == pack.Id);
                        if (packComp != null) {
                            givenPackages.Add(pack);
                            var item = new GiftItem();
                            item.Direction = direction;
                            item.Type = GiftItemType.Obj;
                            item.Obj = packComp.Obj.Data;
                            giftPackage.AddItem(S, item);
                        }
                    }
                }
            }

            const float attChange = 0.05f;
            var facText = T.RelationshipWith_V1.F(Texts.Accented(ship.Faction.NameWithGlyph));
            var acceptText = $"{facText}: {Units.PercentageSigned(attChange, 2, true)}";
            // Handing the package over destroys it, which must not fail the quest. This was
            // cleared while the gift menu was built, so just opening and cancelling the menu
            // disarmed the failure for good.
            giftPackage.OnBeforeGive = (p) => {
                foreach (var pack in givenPackages) {
                    pack.Flags &= ~QuestPackageFlags.FailQuestOnDestroy;
                }
            };
            giftPackage.WithAccept((p) => {
                step.SetState(QuestStepState.Completed);
                ship.ChangeAttitudeTowards(FactionDef.Player, attChange);
            }, GiftPackage.NewConsequence(IconId.CCheck, acceptText));

            ui.AddOption(giftPackage);
            return true;
        }

        public override void OnQuestStateChange(QuestState before, QuestState after) {
            if (after == QuestState.Failed && step.IsActive) {
                FailStep();
            }
        }
    }
}
