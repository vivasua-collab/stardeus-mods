using KL.Signals;
using Game.Systems.Creatures;
using System.Reflection;
using Game.Data.Space;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.AI;
using UnityEngine;
using Game.Scenarios;
using System;
using Game.UI;
using Game.Input;
using UnityEngine.EventSystems;
using Game.Utils;
using Game.Research;
using Game.UI.Research;
using Game.Systems.Drama;
using Game.Constants;
using Game.Systems.Quests;
using Game.Systems.Diplomacy;
using Game.Systems.Ships;
using Game.Systems.Crafting;

namespace Game.Systems.UIUX {
    public class UIQuery {
        public Query1<SpaceObject, bool> ShowQuestHubFor = new(nameof(ShowQuestHubFor));
        // (actions, animate): animate is true for selection changes, false for refreshes
        public Query2<List<EntityActionData>, bool, bool> ShowEntityActions = new(nameof(ShowEntityActions));
        public Query1<BeingData, bool> ShowBeingPanel = new(nameof(ShowBeingPanel));
        public Query1<Tile, bool> ShowTilePanel = new(nameof(ShowTilePanel));
        public Query1<ObjData, bool> ShowObjPanel = new(nameof(ShowObjPanel));
        public Query0<bool> HideEntityPanel = new(nameof(HideEntityPanel));
        /// <summary>
        /// Prevents the next automatic entity-panel close that would be triggered by a
        /// deferred empty selection (e.g. when clicking an expedition being while something
        /// on the map was previously selected). One-shot: the suppression is consumed immediately
        /// by the next EntitySelectionHandler.Update() call.
        /// </summary>
        public Query0<bool> SuppressEntityPanelAutoClose = new(nameof(SuppressEntityPanelAutoClose));
        public Query0<SubPanelWidget> GetEntitySubPanel = new(nameof(GetEntitySubPanel));
        public Query0<bool> CloseEntitySubPanel = new(nameof(CloseEntitySubPanel));
        /// <summary>
        /// int is InventorySlotIdH, List<ObjData> is result.
        /// if InventorySlotIdH is 0, it will return stuff for any equipment slot
        /// returns int count of found items
        /// </summary>
        public Query3<AIAgentComp, int, List<ObjData>, int> FindCompatibleEquipmentUI = new(nameof(FindCompatibleEquipmentUI));
        /// <summary>
        /// Available items for the slot that the agent's being can't use (body or jobs),
        /// returns int count of found items
        /// </summary>
        public Query3<AIAgentComp, int, List<ObjData>, int> FindIncompatibleEquipmentUI = new(nameof(FindIncompatibleEquipmentUI));
        /// <summary>
        /// string = link, returns bool = found and opened
        /// </summary>
        public Query1<string, bool> TryOpenCodexLink = new(nameof(TryOpenCodexLink));
        /// <summary>
        /// Lifetime + codex tooltip request data
        /// </summary>
        public Query2<Lifetime, CodexTooltip, bool> ShowCodexTooltipFor = new(nameof(ShowCodexTooltipFor));
        public Query0<bool> HideCodexTooltip = new(nameof(HideCodexTooltip));
        /// <summary>
        /// def, tooltip widget instance, add/remove, result = added / removed
        /// </summary>
        public Query3<Def, WTooltipCodex, bool, bool> RegisterCodexTooltipFor = new(nameof(RegisterCodexTooltipFor));
        public Query0<bool> TryCloseAllCodexPopups = new(nameof(TryCloseAllCodexPopups));
        /// <summary>
        /// bool = show or hide, if hide, it will drill up one step
        /// </summary>
        public Query1<bool, bool> ShowMainMenu = new(nameof(ShowMainMenu));
        /// <summary>
        /// Hides main menu abruptly without tweening alpha
        /// </summary>
        public Query0<bool> HideMainMenuInstant = new(nameof(HideMainMenuInstant));
        public Query1<bool, bool> ShowQuickSearch = new(nameof(ShowQuickSearch));
        /// <summary>
        /// Closes the main menu regardless of how deep in the hierarchy we are
        /// bool = instant
        /// </summary>
        public Query1<bool, bool> CloseMainMenu = new(nameof(CloseMainMenu));
        /// <summary>
        /// bool = add / remove
        /// </summary>
        public Query3<Lifetime, Tooltip, bool, bool> AddTooltipData = new(nameof(AddTooltipData));
        /// <summary>
        /// Used for editor tooling / debugging only!
        /// </summary>
        public Query0<List<Tooltip>> GetActiveTooltips = new(nameof(GetActiveTooltips));
        /// <summary>
        /// bool = show / hide
        /// </summary>
        public Query2<Scenario, bool, bool> ShowGameSetup = new(nameof(ShowGameSetup));
        /// <summary>
        /// bool = show / hide
        /// </summary>
        public Query1<bool, bool> ShowBuildMenu = new(nameof(ShowBuildMenu));
        // Opens the build menu on the tab that lists the def, for the player to pick it there
        public Query1<Def, bool> ShowBuildMenuFor = new(nameof(ShowBuildMenuFor));

        public Query0<bool> TryCloseTopmostPanel = new(nameof(TryCloseTopmostPanel));

        public Query1<BaseSearchResult, bool> PinQuickSearchResult = new(nameof(PinQuickSearchResult));
        /// <summary>
        /// list of options, on choose. returns false is it's already showing something else
        /// </summary>
        public Query2<List<BeingData>, Action<BeingData>, bool> ShowWakeUpChoice = new(nameof(ShowWakeUpChoice));
        /// <summary>
        /// list of options, on choose. returns false is it's already showing something else
        /// </summary>
        public Query2<List<GiftPackage>, Action<GiftPackage>, bool> ShowGiftChoice = new(nameof(ShowGiftChoice));
        /// <summary>
        /// list of options, on choose. returns false is it's already showing something else
        /// </summary>
        public Query2<List<ShipClassDef>, Action<ShipClassDef>, bool> ShowShipClassChoice = new(nameof(ShowShipClassChoice));
        /// <summary>
        /// list of compatible options, on choose. Cancel button is hidden, player must pick a class.
        /// </summary>
        public Query2<List<ShipClassDef>, Action<ShipClassDef>, bool> ForceShowShipClassChoice = new(nameof(ForceShowShipClassChoice));
        /// <summary>
        /// widget, show (on/off), returns true if worked
        /// </summary>
        public Query2<BaseWidget, bool, bool> ShowPanelModalFor = new(nameof(ShowPanelModalFor));
        public Query0<bool> TryClosePanelModal = new(nameof(TryClosePanelModal));
        public Query1<bool, bool> ShowStarmap = new(nameof(ShowStarmap));
        public Query0<StarmapContext> GetStarmapContext = new(nameof(GetStarmapContext));

        /// <summary>
        /// obj, open if not showing
        /// </summary>
        public Query2<SpaceObject, bool, bool> StarmapFocusOn = new(nameof(StarmapFocusOn));
        public Query1<UISpaceObject, List<UISpaceObject>> StarmapCloseTo = new(nameof(StarmapCloseTo));
        public Query1<Vector2, bool> IsStarmapPosNearMouse = new(nameof(IsStarmapPosNearMouse));
        /// <summary>
        /// bool = was spawned (will set the title)
        /// </summary>
        public Query2<IContextPopup, bool, bool> ShowContextPopup = new(nameof(ShowContextPopup));
        public Query2<IContextPopup, bool, bool> ShowSystemContextPopup = new(nameof(ShowSystemContextPopup));
        public Query1<IContextPopup, bool> CloseContextPopup = new(nameof(CloseContextPopup));

        public Query1<QuickChoice, bool> ShowQuickChoice = new(nameof(ShowQuickChoice));
        public Query0<bool> CancelQuickChoice = new(nameof(CancelQuickChoice));
        public Query0<bool> ToggleInventoryPanel = new(nameof(ToggleInventoryPanel));
        public Query2<Rect, List<BeingData>, bool> TrySelectBeingAvatars = new(nameof(TrySelectBeingAvatars));
        /// <summary>
        /// The being's avatar in the top bar, null when it has none showing
        /// </summary>
        public Query1<BeingData, RectTransform> GetBeingAvatar = new(nameof(GetBeingAvatar));
        /// <summary>
        /// Only for background clicks
        /// </summary>
        public Query1<ActionType, bool> ClickUpInStarmap = new(nameof(ClickUpInStarmap));
        /// <summary>
        /// Bool = refocus, Bool2 = reset parent (when need a full rebuild)
        /// </summary>
        public Query2<bool, bool, bool> RebuildStarmap = new(nameof(RebuildStarmap));
        /// <summary>
        /// bool = show / hide, bool2 = for starmap
        /// </summary>
        public Query2<bool, bool, bool> ToggleGameUI = new(nameof(ToggleGameUI));
        public Query2<WQuickSearchPinnedItem, Vector2, bool> OnDragQuickSearchPinnedItem = new(nameof(OnDragQuickSearchPinnedItem));
        public Query1<bool, bool> ShowResearch = new(nameof(ShowResearch));
        public Query0<Dictionary<string, ResearchTreeNode>> GetResearchLayout = new(nameof(GetResearchLayout));
        public Query1<Def, bool> ShowInResearchTree = new(nameof(ShowInResearchTree));

        public Query0<bool> RebuildResearchMenu = new(nameof(RebuildResearchMenu));
        public Query1<BaseEntityTab, bool> ReloadEntityTab = new(nameof(ReloadEntityTab));
        public Query0<bool> ReloadCurrentEntity = new(nameof(ReloadCurrentEntity));
        public Query1<Entity, bool> ReloadEntityIfSelected = new(nameof(ReloadEntityIfSelected));
        public Query0<WidgetRegistry> GetWidgetRegistry = new(nameof(GetWidgetRegistry));
        public Query1<bool, bool> ShowExpeditions = new(nameof(ShowExpeditions));
        public Query0<bool> RebuildExpeditions = new(nameof(RebuildExpeditions));
        public Query1<Expedition, bool> ShowExpedition = new(nameof(ShowExpedition));
        public Query1<Expedition, bool> ToggleExpedition = new(nameof(ToggleExpedition));
        public Query1<SpaceObject, bool> StartExpeditionTo = new(nameof(StartExpeditionTo));
        public Query1<IDraggable, bool> StartDragging = new(nameof(StartDragging));
        public Query0<(List<SocialInteraction> list, B<SocialInteraction> onAdd)> GetSocialInteractions = new(nameof(GetSocialInteractions));
        public Query1<MatType, BInt> GetBindableMatCountFor    = new(nameof(GetBindableMatCountFor));
        public Query1<Def, BInt>     GetBindableObjCountFor   = new(nameof(GetBindableObjCountFor));
        public Query1<Def, BInt>     GetBindableBeingCountFor = new(nameof(GetBindableBeingCountFor));
        public Query1<Def, BInt>     GetBindableProductionRate  = new(nameof(GetBindableProductionRate));
        public Query1<Def, BInt>     GetBindableConsumptionRate = new(nameof(GetBindableConsumptionRate));
        public Query0<bool> RebuildEntityActions = new(nameof(RebuildEntityActions));
        public Query0<bool> TryFocusSearchField = new(nameof(TryFocusSearchField));
        public Query1<bool, bool> ShowQuests = new(nameof(ShowQuests));
        public Query1<Quest, bool> ShowQuest = new(nameof(ShowQuest));
        public Query1<Quest, bool> ShowNewQuest = new(nameof(ShowNewQuest));
        public Query1<Quest, bool> ToggleQuest = new(nameof(ToggleQuest));
        public Query1<List<Quest>, bool> ShowQuestHubQuests = new(nameof(ShowQuestHubQuests));
        public Query1<bool, bool> ToggleNotes = new(nameof(ToggleNotes));
        public Query1<string, bool> ShowTutorial = new(nameof(ShowTutorial));
        public Query2<string, float, bool> ShowTutorialPopup = new(nameof(ShowTutorialPopup));
        // Whether a tutorial popup for this codex page id hash is queued or on screen
        public Query1<int, bool> IsTutorialPopupPending = new(nameof(IsTutorialPopupPending));
        // Whether the HUD reveal shows this key (a bottom bar tool id, "research", "app.Log"...)
        public Query1<string, bool> IsHudRevealed = new(nameof(IsHudRevealed));
        // True once after a reveal wave added Ship Computer apps: the Apps tab rings itself
        public Query0<bool> TakeNewAppsHint = new(nameof(TakeNewAppsHint));
        // True once per build menu tab after the reveal showed it: the tab rings itself
        public Query1<string, bool> TakeNewBuildTabHint = new(nameof(TakeNewBuildTabHint));
        // Reveals a hidden key for good, as using it does (the Codex opening a build tab)
        public Query1<string, bool> RevealHudKey = new(nameof(RevealHudKey));
        public Query0<bool> CloseTutorial = new(nameof(CloseTutorial));
        public Query1<string, bool> SetNoteToSelf = new(nameof(SetNoteToSelf));
        public Query1<bool, bool> ToggleSurgeries = new(nameof(ToggleSurgeries));
        public Query1<int, bool> ToggleSurgeriesFiltered = new(nameof(ToggleSurgeriesFiltered));
        public Query1<bool, bool> TogglePolicies =new(nameof(TogglePolicies));
        public Query1<BeingData, bool> ToggleSurgeriesFor = new(nameof(ToggleSurgeriesFor));
        public Query1<BeingData, bool> ShowSurgeriesFor = new(nameof(ShowSurgeriesFor));
        public Query1<BeingData, bool> ShowPoliciesFor = new(nameof(ShowPoliciesFor));
        public Query1<bool, bool> ToggleSituation = new(nameof(ToggleSituation));
        public Query1<bool, bool> ToggleCombatControls = new(nameof(ToggleCombatControls));
        public Query1<string, bool> ToggleMinigame = new(nameof(ToggleMinigame));
        public Query1<bool, bool> ToggleLog = new(nameof(ToggleLog));
        public Query0<bool> RebuildLog = new(nameof(RebuildLog));
        public Query1<bool, bool> ToggleCodex = new(nameof(ToggleCodex));
        public Query1<bool, bool> ToggleJobMatrix = new(nameof(ToggleJobMatrix));
        public Query1<CodexConceptPage, bool> ShowCodexEntry = new(nameof(ShowCodexEntry));
        public Query1<bool, bool> ToggleShipConfig = new(nameof(ToggleShipConfig));
        public Query0<bool> RebuildShipConfig = new(nameof(RebuildShipConfig));
        public Query1<bool, bool> ToggleTerraformConfig = new(nameof(ToggleTerraformConfig));
        public Query0<bool> RebuildTerraformConfig = new(nameof(RebuildTerraformConfig));
        public Query1<bool, bool> ToggleWallet = new(nameof(ToggleWallet));
        public Query0<bool> RebuildWallet = new(nameof(RebuildWallet));
        public Query1<bool, bool> ToggleElectricity = new(nameof(ToggleElectricity));
        public Query0<bool> RebuildElectricity = new(nameof(RebuildElectricity));
        public Query1<bool, bool> ToggleTwitch = new(nameof(ToggleTwitch));
        public Query1<bool, bool> ToggleProduction = new(nameof(ToggleProduction));
        public Query1<bool, bool> ToggleDiplomacy = new(nameof(ToggleDiplomacy));
        public Query1<bool, bool> ToggleShipCombat = new(nameof(ToggleShipCombat));
        public Query1<bool, bool> ToggleShipCombatMini = new(nameof(ToggleShipCombatMini));
        public Query1<string, bool> ShowSelectedEntityTab = new(nameof(ShowSelectedEntityTab));
        public Query0<bool> ResetSelectedEntityTab = new(nameof(ResetSelectedEntityTab));
        public Query1<bool, bool> ToggleDevMenu = new(nameof(ToggleDevMenu));
        public Query1<string, bool> ShowDevMenuTab = new(nameof(ShowDevMenuTab));
        public Query1<Craftable, BCallback> GetCraftableChangedCallback = new(nameof(GetCraftableChangedCallback));
        public Query1<CraftingOrder, bool> OpenCraftingOrderConfig = new(nameof(OpenCraftingOrderConfig));
        public Query0<CraftingOrder> GetPendingCraftingOrderToShow = new(nameof(GetPendingCraftingOrderToShow));
        public Query1<string, bool> ShowMainMenuSettingsPage = new(nameof(ShowMainMenuSettingsPage));
        public Query0<bool> ShowChangelog = new(nameof(ShowChangelog));
    }
}
