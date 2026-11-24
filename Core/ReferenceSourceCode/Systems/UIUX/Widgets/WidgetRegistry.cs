using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class WidgetRegistry {
        public readonly TopBarWidget TopBar;
        public readonly BottomBarWidget BottomBar;
        public readonly TopRightWidget TopRight;
        public readonly InventoryWidget Inventory;
        public readonly QuestWidget Quest;
        public readonly QuestsWidget Quests;
        public readonly GameSetupWidget GameSetup;
        public readonly BeingsListWidget BeingsList;
        public readonly EntityPanelWidget EntityPanel;
        public readonly EntityPanelMultiWidget EntityPanelMulti;
        public readonly InventoryPanelWidget InventoryConfig;
        public readonly NotificationHubWidget NotificationHub;
        public readonly OnScreenAlertWidget OnScreenAlerts;
        public readonly MainMenuWidget MainMenu;
        public readonly BuildMenuWidget BuildMenu;
        public readonly QuickSearchWidget QuickSearch;
        public readonly PanelContainerWidget PanelContainer;
        public readonly WakeUpChoiceWidget WakeUpChoice;
        public readonly GiftChoiceWidget GiftChoice;
        public readonly ShipClassChoiceWidget ShipClassChoice;
        public readonly PanelModalWidget PanelModal;
        public readonly StarmapWidget Starmap;
        public readonly ResearchWidget Research;
        public readonly QuickChoiceWidget QuickChoice;
        public readonly HyperspaceWidget Hyperspace;
        public readonly ExpeditionsWidget Expeditions;
        public readonly CodexWidget Codex;
        public readonly ShipConfigWidget ShipConfig;
        public readonly JobMatrixWidget JobMatrix;
        public readonly TerraformConfigWidget TerraformConfig;
        public readonly WalletWidget Wallet;
        public readonly ElectricityWidget Electricity;
        public readonly NotepadWidget Notepad;
        public readonly LogWidget Log;
        public readonly SituationWidget Situation;
        public readonly TwitchWidget Twitch;
        public readonly TutorialWidget Tutorial;
        public readonly ShipCombatMiniWidget ShipCombatMini;
        public readonly ShipCombatOurWidget ShipCombatOur;
        public readonly ShipCombatTheirWidget ShipCombatTheir;
        public readonly ShipCombatStationsWidget ShipCombatStations;
        public readonly SurgeriesWidget Surgeries;
        public readonly PoliciesWidget Policies;
        public readonly DiplomacyWidget Diplomacy;
        public readonly ProductionWidget Production;
        #if !STARDEUS_DEMO
        public readonly DevMenuWidget DevMenu;
        #endif
        public WidgetRegistry() {
            D.Log("Ready to toggle all widgets");
            TopBar = Object.FindAnyObjectByType<TopBarWidget>();
            BottomBar = Object.FindAnyObjectByType<BottomBarWidget>();
            TopRight = Object.FindAnyObjectByType<TopRightWidget>();
            Inventory = Object.FindAnyObjectByType<InventoryWidget>();
            EntityPanel = Object.FindAnyObjectByType<EntityPanelWidget>(FindObjectsInactive.Include);
            EntityPanelMulti = Object.FindAnyObjectByType<EntityPanelMultiWidget>(FindObjectsInactive.Include);
            BeingsList = Object.FindAnyObjectByType<BeingsListWidget>();
            MainMenu = Object.FindAnyObjectByType<MainMenuWidget>();
            BuildMenu = Object.FindAnyObjectByType<BuildMenuWidget>();
            Quest = Object.FindAnyObjectByType<QuestWidget>();
            Quests = Object.FindAnyObjectByType<QuestsWidget>();
            QuickSearch = Object.FindAnyObjectByType<QuickSearchWidget>();
            QuickChoice = Object.FindAnyObjectByType<QuickChoiceWidget>();
            PanelContainer = Object.FindAnyObjectByType<PanelContainerWidget>();
            GameSetup = Object.FindAnyObjectByType<GameSetupWidget>();
            InventoryConfig = Object.FindAnyObjectByType<InventoryPanelWidget>(FindObjectsInactive.Include);
            NotificationHub = Object.FindAnyObjectByType<NotificationHubWidget>();
            OnScreenAlerts = Object.FindAnyObjectByType<OnScreenAlertWidget>();
            WakeUpChoice = Object.FindAnyObjectByType<WakeUpChoiceWidget>();
            GiftChoice = Object.FindAnyObjectByType<GiftChoiceWidget>();
            ShipClassChoice = Object.FindAnyObjectByType<ShipClassChoiceWidget>();
            PanelModal = Object.FindAnyObjectByType<PanelModalWidget>(FindObjectsInactive.Include);
            Starmap = Object.FindAnyObjectByType<StarmapWidget>();
            Research = Object.FindAnyObjectByType<ResearchWidget>();
            Hyperspace = Object.FindAnyObjectByType<HyperspaceWidget>();
            Expeditions = Object.FindAnyObjectByType<ExpeditionsWidget>();
            Codex = Object.FindAnyObjectByType<CodexWidget>(FindObjectsInactive.Include);
            JobMatrix = Object.FindAnyObjectByType<JobMatrixWidget>(FindObjectsInactive.Include);
            ShipConfig = Object.FindAnyObjectByType<ShipConfigWidget>(FindObjectsInactive.Include);
            TerraformConfig = Object.FindAnyObjectByType<TerraformConfigWidget>(FindObjectsInactive.Include);
            Wallet = Object.FindAnyObjectByType<WalletWidget>(FindObjectsInactive.Include);
            Electricity = Object.FindAnyObjectByType<ElectricityWidget>(FindObjectsInactive.Include);
            Notepad = Object.FindAnyObjectByType<NotepadWidget>(FindObjectsInactive.Include);
            Log = Object.FindAnyObjectByType<LogWidget>(FindObjectsInactive.Include);
            Situation = Object.FindAnyObjectByType<SituationWidget>(FindObjectsInactive.Include);
            Twitch = Object.FindAnyObjectByType<TwitchWidget>(FindObjectsInactive.Include);
            Tutorial = Object.FindAnyObjectByType<TutorialWidget>(FindObjectsInactive.Include);
            ShipCombatMini = Object.FindAnyObjectByType<ShipCombatMiniWidget>(FindObjectsInactive.Include);
            ShipCombatOur = Object.FindAnyObjectByType<ShipCombatOurWidget>(FindObjectsInactive.Include);
            ShipCombatTheir = Object.FindAnyObjectByType<ShipCombatTheirWidget>(FindObjectsInactive.Include);
            ShipCombatStations = Object.FindAnyObjectByType<ShipCombatStationsWidget>(FindObjectsInactive.Include);
            Surgeries = Object.FindAnyObjectByType<SurgeriesWidget>(FindObjectsInactive.Include);
            Policies = Object.FindAnyObjectByType<PoliciesWidget>(FindObjectsInactive.Include);
            Production = Object.FindAnyObjectByType<ProductionWidget>(FindObjectsInactive.Include);
            Diplomacy = Object.FindAnyObjectByType<DiplomacyWidget>(FindObjectsInactive.Include);

            #if !STARDEUS_DEMO
            DevMenu = Object.FindAnyObjectByType<DevMenuWidget>(FindObjectsInactive.Include);
            #endif
        }

        public void ToggleAll(bool on, bool forStarmap) {
            D.Warn("Toggling all widgets: {0} (for starmap: {1})", on, forStarmap);
            Toggle(TopBar, on);
            Toggle(BottomBar, on);
            Toggle(TopRight, on);
            Toggle(BeingsList, on);

            // If we toggle all, we won't show dialog / popups!
            // Toggle(PanelContainer, on);

            // parts of panel container but we still want to toggle them
            if (!forStarmap) {
                // for starmap, we want to keep the hub, it includes jump button
                Toggle(NotificationHub, on);
                Toggle(Expeditions, on);
                Toggle(Quest, on);
                Toggle(Quests, on);
                Toggle(ShipConfig, on);
                Toggle(Wallet, on);
            }
            Toggle(EntityPanel, on);
            Toggle(EntityPanelMulti, on);
            Toggle(Inventory, on);
            Toggle(BuildMenu, on);
            Toggle(QuickSearch, on);
            Toggle(Codex, on);
            Toggle(JobMatrix, on);
            Toggle(Electricity, on);
            Toggle(Log, on);
            Toggle(Twitch, on);
            Toggle(ShipCombatMini, on);
            Toggle(ShipCombatOur, on);
            Toggle(ShipCombatTheir, on);
            Toggle(ShipCombatStations, on);
            Toggle(Surgeries, on);
            Toggle(Policies, on);

            // don't toggle on screen alerts
            // Toggle(OnScreenAlerts, on);
        }

        private void Toggle(MonoBehaviour mb, bool on) {
            if (!mb.TryGetComponent<CanvasGroup>(out var cg)) {
                D.Err("Widget has no CanvasGroup! {0} | {1}",
                    mb, D.GOPath(mb.transform));
                return;
            }
            UIUtils.ToggleCG(cg, on);
        }
    }
}
