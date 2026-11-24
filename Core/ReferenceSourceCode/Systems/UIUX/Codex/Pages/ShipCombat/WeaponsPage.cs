using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class WeaponsPage : CodexConceptPage {
        public const string PageId = "ShipCombat/Weapons";
        public override string Id => PageId;
        public override string TitleT => T.NounShipWeapons;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeapons1_V1.F(
                Texts.Accented(T.NounShipWeapons)), idx++);
            AddTextBlock(life, g, T.CodexMWeaponsBattlestations_V1.F(
                CodexManualLink(T.NounBattleStationPlural, BattleStationsPage.PageId)
            ), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeaponsBallistic1_V3.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShredderCannon, T.ResearchShipcombatWeaponsballistic),
                CodexManualLink(T.NounShields, ShieldsPage.PageId),
                CodexManualLink(T.FluxHard, FluxPage.PageId)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeaponsBallistic2_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShredderCannon, T.ResearchShipcombatWeaponsballistic),
                DefLink(DefIds.ObjAsteroidsAsteroid01)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeaponsLaser1_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesHeavyLaser, T.ResearchShipcombatWeaponslaser),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShredderCannon, T.ResearchShipcombatWeaponsballistic)),
                idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeaponsMissile1_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesRocketPod, T.ResearchShipcombatWeaponsmissile),
                CodexManualLink(T.NounShields, ShieldsPage.PageId)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeaponsPointDefense1_V3.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesInterceptorTurret, T.ResearchShipcombatPointdefense),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesRocketPod, T.ResearchShipcombatWeaponsmissile),
                DefLink(DefIds.ObjContainersBreachCapsuleFlying)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesWeaponsPlasma1_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesGaussStriker, T.ResearchShipcombatWeaponsplasma),
                CodexManualLink(T.NounShields, ShieldsPage.PageId)), idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutShipCombatStepDevicesWeapons1_V1,
                T.ResearchShipcombatWeaponsballistic, T.ResearchShipcombatWeaponslaser,
                T.ResearchShipcombatWeaponsmissile, T.ResearchShipcombatPointdefense,
                T.ResearchShipcombatWeaponsplasma);
        }
    }
}
