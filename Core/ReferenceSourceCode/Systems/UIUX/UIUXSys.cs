using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class UIUXSys : GameSystem, ISaveableSpecial {
        public const string SysId = "UIUX";
        public override string Id => SysId;
        private UIUXSysData data;
        private CodexSysData codexData;
        private TutorialSysData tutorialData;
        private WidgetRegistry registry;
        private TopBarHandler topBarHandler;
        private BottomBarHandler bottomBarHandler;
        private InventoryHandler inventoryHandler;
        private QuestHandler questHandler;
        private QuestsHandler questsHandler;
        private BeingsListHandler beingsList;
        private InventoryPanelHandler inventoryPanelHandler;
        private EntityPanelHandler entityPanelHandler;
        private EntitySelectionHandler selectionHandler;
        private OnScreenAlertHandler onScreenAlertHandler;
        private CodexPopupHandler codexPopupHandler;
        private TooltipHandler tooltipHandler;
        private TopRightHandler topRightHandler;
        private GameSetupHandler gameSetupHandler;
        private MainMenuHandler mainMenuHandler;
        private BuildMenuHandler buildMenuHandler;
        private QuickSearchHandler quickSearchHandler;
        private PanelContainerHandler panelContainerHandler;
        private WakeUpChoiceHandler wakeUpChoiceHandler;
        private GiftChoiceHandler giftChoiceHandler;
        private ShipClassChoiceHandler shipClassChoiceHandler;
        private PanelModalHandler panelModalHandler;
        private StarmapHandler starmapHandler;
        private ResearchHandler researchHandler;
        private HyperspaceHandler hyperspaceHandler;
        private ExpeditionsHandler expeditionsHandler;
        private CodexHandler codexHandler;
        private DragHandler dragHandler;
        private QuickChoiceHandler quickChoiceHandler;
        private JobMatrixHandler jobMatrixHandler;
        private ShipConfigHandler shipConfigHandler;
        private TerraformConfigHandler terraformConfigHandler;
        private WalletHandler walletHandler;
        private ContextPopupHandler contextPopupHandler;
        private ElectricityHandler electricityHandler;
        private NotepadHandler notepadHandler;
        private LogHandler logHandler;
        private SituationHandler situationHandler;
        private TwitchHandler twitchHandler;
        private TutorialHandler tutorialHandler;
        private SurgeriesHandler surgeriesHandler;
        private PoliciesHandler policiesHandler;
        private ShipCombatModeHandler shipCombatModeHandler;
        private ProductionHandler productionHandler;
        private DiplomacyHandler diplomacyHandler;
        private CombatControlsHandler combatControlsHandler;
        private HudRevealHandler hudRevealHandler;
#if !STARDEUS_DEMO
        private DevMenuHandler devMenuHandler;
#endif
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new UIUXSys());
        }

        public override void Unload() {
            onScreenAlertHandler?.Cleanup();
            topBarHandler?.Cleanup();
            buildMenuHandler?.Cleanup();
            quickSearchHandler?.Cleanup();
            starmapHandler?.Cleanup();
            researchHandler?.Cleanup();
            hyperspaceHandler?.Cleanup();
            tooltipHandler?.Cleanup();
            questHandler?.Cleanup();
            tutorialHandler?.Cleanup();
            jobMatrixHandler?.Cleanup();
            contextPopupHandler?.Cleanup();
            shipCombatModeHandler?.Cleanup();
            electricityHandler?.Cleanup();
            walletHandler?.Cleanup();
            situationHandler?.Cleanup();
            combatControlsHandler?.Cleanup();
        }

        protected override void OnInitialize() {
            registry = new WidgetRegistry();

            S.UIQuery.GetWidgetRegistry.SetResponder(() => registry);

            topBarHandler = new TopBarHandler(S, registry.TopBar);
            bottomBarHandler = new BottomBarHandler(S, registry.BottomBar);
            inventoryHandler = new InventoryHandler(S, registry.Inventory);
            onScreenAlertHandler = new OnScreenAlertHandler(S, registry.OnScreenAlerts);
            inventoryPanelHandler = new InventoryPanelHandler(S, registry.InventoryConfig);
            beingsList = new BeingsListHandler(S, registry.BeingsList);
            questHandler = new QuestHandler(S, registry.Quest);
            questsHandler = new QuestsHandler(S, registry.Quests);
            entityPanelHandler = new EntityPanelHandler(S, registry.EntityPanel);
            selectionHandler = new EntitySelectionHandler(S, registry.EntityPanelMulti);
            codexPopupHandler = new CodexPopupHandler(S);
            tooltipHandler = new TooltipHandler(S, registry.MainMenu.MouseTooltip);
            gameSetupHandler = new GameSetupHandler(S, registry.GameSetup);
            mainMenuHandler = new MainMenuHandler(S, registry.MainMenu);
            buildMenuHandler = new BuildMenuHandler(S, registry.BuildMenu);
            quickSearchHandler = new QuickSearchHandler(S, registry.QuickSearch);
            topRightHandler = new TopRightHandler(S, registry.TopRight);
            hudRevealHandler = new HudRevealHandler(S, registry.BottomBar, registry.TopBar,
                registry.TopRight);
            panelContainerHandler = new PanelContainerHandler(S, registry.PanelContainer);
            wakeUpChoiceHandler = new WakeUpChoiceHandler(S, registry.WakeUpChoice);
            giftChoiceHandler = new GiftChoiceHandler(S, registry.GiftChoice);
            shipClassChoiceHandler = new ShipClassChoiceHandler(S, registry.ShipClassChoice);
            panelModalHandler = new PanelModalHandler(S, registry.PanelModal);
            starmapHandler = new StarmapHandler(S, registry.Starmap);
            researchHandler = new ResearchHandler(S, registry.Research);
            quickChoiceHandler = new QuickChoiceHandler(S, registry.QuickChoice);
            hyperspaceHandler = new HyperspaceHandler(S, registry.Hyperspace);
            expeditionsHandler = new ExpeditionsHandler(S, registry.Expeditions);
            codexHandler = new CodexHandler(S, registry.Codex);
            shipConfigHandler = new ShipConfigHandler(S, registry.ShipConfig);
            jobMatrixHandler = new JobMatrixHandler(S, registry.JobMatrix);
            terraformConfigHandler = new TerraformConfigHandler(S, registry.TerraformConfig);
            walletHandler = new WalletHandler(S, registry.Wallet);
            electricityHandler = new ElectricityHandler(S, registry.Electricity);
            notepadHandler = new NotepadHandler(S, registry.Notepad);
            logHandler = new LogHandler(S, registry.Log);
            situationHandler = new SituationHandler(S, registry.Situation);
            twitchHandler = new TwitchHandler(S, registry.Twitch);
            tutorialHandler = new TutorialHandler(S, registry.Tutorial);
            surgeriesHandler = new SurgeriesHandler(S, registry.Surgeries);
            policiesHandler = new PoliciesHandler(S, registry.Policies);
            productionHandler = new ProductionHandler(S, registry.Production);
            diplomacyHandler = new DiplomacyHandler(S, registry.Diplomacy);
            combatControlsHandler = new CombatControlsHandler(S);
            shipCombatModeHandler = new ShipCombatModeHandler(S, registry.ShipCombatMini
                , registry.ShipCombatOur, registry.ShipCombatTheir, registry.ShipCombatStations);
#if !STARDEUS_DEMO
            devMenuHandler = new DevMenuHandler(S, registry.DevMenu);
#endif
            contextPopupHandler = new ContextPopupHandler(S,
                ResourceLibrary.FindPrefab<ContextPopupWidget>(
                    UIPrefabId.ContextPopupWidget));
            dragHandler = new DragHandler(S);

            S.UIQuery.ToggleGameUI.SetResponder(ToggleGameUI);
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.OnLateUpdate.AddListener(OnLateUpdate);

            S.Sig.AfterLoadState.AddListener(OnLoad);
        }

        private void OnLoad(GameState s) {
            codexHandler.OnLoad(codexData);
            tutorialHandler.OnLoad(tutorialData);
            if (data == null) { return; }
            quickSearchHandler.OnLoad(data);
            notepadHandler.OnLoad(data);
            inventoryHandler.OnLoad(data);
            buildMenuHandler.OnLoad(data);
            data = null;
        }

        private bool ToggleGameUI(bool on, bool forStarmap) {
            registry.ToggleAll(on, forStarmap);
            return true;
        }

        private void OnLateUpdate() {
            dragHandler.Update();
            starmapHandler.Update();
            researchHandler.Update();
            onScreenAlertHandler.Update();
            selectionHandler.Update();
            entityPanelHandler.Update();
            tooltipHandler.Update();
            quickChoiceHandler.Update();
            codexHandler.Update();
            tutorialHandler.Update();
            logHandler.Update();
            hudRevealHandler.Update();
        }

        private void OnLoadState(GameState s) {
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.UIUX = new UIUXSysData();
            quickSearchHandler.OnSave(sd.UIUX);
            codexHandler.OnSave(sd);
            tutorialHandler.OnSave(sd);
            notepadHandler.OnSave(sd.UIUX);
            inventoryHandler.OnSave(sd.UIUX);
            buildMenuHandler.OnSave(sd.UIUX);
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd?.UIUX;
            codexData = sd?.Codex;
            tutorialData = sd?.Tutorial;
        }
    }
}
