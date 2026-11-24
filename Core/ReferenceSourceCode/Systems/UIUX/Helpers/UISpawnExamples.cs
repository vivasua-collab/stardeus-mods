using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public static class UISpawnExamples {
        /*
         * Prefab background/usage guideline:
         * - WContentSomething produces light blue background and is usually for informational messages.
         * - WContent2Something produces darker background and is usually for interactable blocks.
         * - We almost never want to mix WContentX and WContent2X in the same block.
         * - A Header (WContent2Header / WContentHeader) can only appear once, at the very
         *   beginning of a block. If you need multiple headers, create multiple blocks.
         *   We almost always want a header as the first widget in any UI block.
         */
        public static readonly IReadOnlyDictionary<string, Type> TypeByPrefabId = new Dictionary<string, Type> {
            { UIPrefabId.WAppButton, typeof(WAppButton) },
            { UIPrefabId.WBeingListItem, typeof(WBeingListItem) },
            { UIPrefabId.WBeingListItem2, typeof(WBeingListItem2) },
            { UIPrefabId.WBodyPart, typeof(WBodyPart) },
            { UIPrefabId.WBuildMenuButton, typeof(WButtonIconText) },
            { UIPrefabId.WBuildMenuButtonExpandable, typeof(WBuildMenuButtonExpandable) },
            { UIPrefabId.WButtonPopoutIconTextSlider, typeof(WSliderWithIconText2) },
            { UIPrefabId.WButtonPopoutInput, typeof(WInputWithLabel) },
            { UIPrefabId.WButtonPopoutItem, typeof(WButtonText) },
            { UIPrefabId.WButtonPopoutLabel, typeof(WText) },
            { UIPrefabId.WButtonPopoutToggle, typeof(WToggleText) },
            { UIPrefabId.WContent2Button, typeof(WButtonIconText2) },
            { UIPrefabId.WContent2Button_Being_Button2, typeof(WBeingButton2) },
            { UIPrefabId.WContent2Button2, typeof(WContentButton2) },
            { UIPrefabId.WContent2Button2Text2, typeof(WContentButton2Text2) },
            { UIPrefabId.WContent2ButtonText3, typeof(WButtonIconText2WithLabel) },
            { UIPrefabId.WContent2ButtonToggle, typeof(WButtonIconText2WithToggle) },
            { UIPrefabId.WContent2ButtonSimple, typeof(WButtonText) },
            { UIPrefabId.WContent2ButtonX, typeof(WContentButtonWithX) },
            { UIPrefabId.WContent2Dropdown, typeof(WDropdownWithIcon) },
            { UIPrefabId.WContent2Header, typeof(WText) },
            { UIPrefabId.WContent2HeaderButton, typeof(WButtonText2) },
            { UIPrefabId.WContent2HeaderButtonIcon, typeof(WButtonIconText) },
            { UIPrefabId.WContent2HeaderProgress, typeof(WProgressWithLabel) },
            { UIPrefabId.WContent2ListButton, typeof(WButtonIconText2) },
            { UIPrefabId.WContent2ListButtonAmt, typeof(WContentListButtonWithAmt) },
            { UIPrefabId.WContent2Progress, typeof(WIconText2Progress) },
            { UIPrefabId.WContent2ProgressButton, typeof(WButtonIconText2Progress) },
            { UIPrefabId.WContent2Input, typeof(WInputWithIcon) },
            { UIPrefabId.WContent2TextArea, typeof(WInput) },
            { UIPrefabId.WContent2Text, typeof(WIconText) },
            { UIPrefabId.WContent2Text2, typeof(WIconText2) },
            { UIPrefabId.WContent2Text2Toggle, typeof(WToggleWithIconText2) },
            { UIPrefabId.WContent2Text2Btn, typeof(WButtonWithIconText2) },
            { UIPrefabId.WContent2Text2Amt, typeof(WInputAmtIconText2) },
            { UIPrefabId.WContent2Text2AmtToggle, typeof(WInputAmtText2Toggle) },
            { UIPrefabId.WContent2Text2Slider, typeof(WSliderWithIconText2) },
            { UIPrefabId.WContent2Text2SliderRange, typeof(WSliderRangeWithIconText2) },
            { UIPrefabId.WContent2TextBlock, typeof(WText) },
            { UIPrefabId.WContent2Toggle, typeof(WToggleText) },
            { UIPrefabId.WContent3Button, typeof(WContent3Button) },
            { UIPrefabId.WContent3Button_Sortable, typeof(WContent3Button) },
            { UIPrefabId.WContent3ButtonSimple, typeof(WButtonText) },
            { UIPrefabId.WContent3IconButtonSimple, typeof(WButtonIconText) },
            { UIPrefabId.WContent3IconTextSimple, typeof(WIconText) },
            { UIPrefabId.WContent4Button, typeof(WButtonIconText2) },
            { UIPrefabId.WContent4Priority, typeof(WPriorityUpDown) },
            { UIPrefabId.WContent4Toggle, typeof(WToggleText) },
            { UIPrefabId.WContentBlock, typeof(WContentBlock) },
            { UIPrefabId.WContentBlock_FreeHeight, typeof(WGroup) },
            { UIPrefabId.WContentBlock_Grid, typeof(WContentBlock) },
            { UIPrefabId.WContentBlock_SidePadded, typeof(WContentBlock) },
            { UIPrefabId.WContentHeader, typeof(WText) },
            { UIPrefabId.WContentHeaderButton, typeof(WButtonText2) },
            { UIPrefabId.WContentHeaderButtonIcon, typeof(WButtonIconText) },
            { UIPrefabId.WContentHeaderExpandable, typeof(WContentListButton) },
            { UIPrefabId.WContentHeaderExpandableButton, typeof(WContentListButtonWithButton) },
            { UIPrefabId.WContentHeaderProgress, typeof(WProgressWithLabel) },
            { UIPrefabId.WContentHeaderProgress_Green, typeof(WProgressWithLabel) },
            { UIPrefabId.WContentListButton, typeof(WButtonIconText2) },
            { UIPrefabId.WContentProgress, typeof(WIconText2Progress) },
            { UIPrefabId.WContentText, typeof(WIconText) },
            { UIPrefabId.WContentText2, typeof(WIconText2) },
            { UIPrefabId.WContentText2Amt, typeof(WInputAmtIconText2) },
            { UIPrefabId.WContentTextBlock, typeof(WText) },
            { UIPrefabId.WContentTextBlockIcon, typeof(WIconText) },
            { UIPrefabId.WContentTextButton, typeof(WButtonIconText2) },
            { UIPrefabId.WContentTextX, typeof(WContentTextWithX) },
            { UIPrefabId.WEntityDetailsPopup, typeof(WEntityDetailsPopup) },
            { UIPrefabId.WExpeditionItem, typeof(WExpeditionItem) },
            { UIPrefabId.WExpeditionMini, typeof(WExpeditionMini) },
            { UIPrefabId.WFaction, typeof(WFaction) },
            { UIPrefabId.WFavMat, typeof(WButtonIconText) },
            { UIPrefabId.WFavMatWide, typeof(WButtonIconText2) },
            { UIPrefabId.WFooterItem_ConfirmButton, typeof(WButtonText) },
            { UIPrefabId.WFooterItem_DeleteButton, typeof(WButtonText) },
            { UIPrefabId.WFooterItem_RedButton, typeof(WButtonText) },
            { UIPrefabId.WFooterItem_Slider, typeof(WSliderWithLabel) },
            { UIPrefabId.WFooterItem_Toggle, typeof(WToggleText) },
            { UIPrefabId.WIndentWrapper, typeof(WIndentWrapper) },
            { UIPrefabId.WInventoryItem, typeof(WButtonIconText2) },
            { UIPrefabId.WInventoryMatItem, typeof(WInventoryMatItem) },
            { UIPrefabId.WListChoicePopup, typeof(WPanel) },
            { UIPrefabId.WMainMenuButton, typeof(WButtonText) },
            { UIPrefabId.WMainMenuButton2, typeof(WButtonText) },
            { UIPrefabId.WMainMenuButton3, typeof(WButtonText) },
            { UIPrefabId.WMainMenuButtonSub, typeof(WButtonText2) },
            { UIPrefabId.WMainMenuButtonSub_Toggle, typeof(WButtonText2WithToggle) },
            { UIPrefabId.WMainMenuSideButton, typeof(WButtonIconText) },
            { UIPrefabId.WMainMenuSubHeader, typeof(WText) },
            { UIPrefabId.WMainMenuTab, typeof(WTabButton) },
            { UIPrefabId.WMainMenuTab2, typeof(WTabButton) },
            { UIPrefabId.WMenuPanelButton, typeof(WButtonText) },
            { UIPrefabId.WPanelButton, typeof(WButtonText) },
            { UIPrefabId.WPanelButton_Red, typeof(WButtonText) },
            { UIPrefabId.WPanelButton_Confirm, typeof(WButtonText) },
            { UIPrefabId.WPanelButtonIcon, typeof(WButtonIconText) },
            { UIPrefabId.WQuickSearchButton, typeof(WQuickSearchPinnedItem) },
            { UIPrefabId.WQuickSearchResult, typeof(WQuickSearchResult) },
            { UIPrefabId.WQuickSearchResult_Small, typeof(WQuickSearchResult) },
            { UIPrefabId.WQuickSearchResult_Unclickable, typeof(WQuickSearchResult) },
            { UIPrefabId.WSpacer, typeof(WSpacer) },
            { UIPrefabId.WGameApp, typeof(WGameApp) },
            { UIPrefabId.WIcon, typeof(WImage) },
            { UIPrefabId.WStatusIcon, typeof(WImage) },
            { UIPrefabId.WStatusIcon_NoRaycast, typeof(WImage) },
            { UIPrefabId.WTabButton_Narrow, typeof(WTabButton) },
            { UIPrefabId.WTabButtonVertical, typeof(WTabButton) },
            { UIPrefabId.WTabPanelItem_Button, typeof(WButtonText) },
            { UIPrefabId.WTabPanelItem_ButtonImageBlock, typeof(WButtonIcon) },
            { UIPrefabId.WTabPanelItem_Dropdown, typeof(WDropdownWithLabel) },
            { UIPrefabId.WTabPanelItem_Header, typeof(WText) },
            { UIPrefabId.WTabPanelItem_Input, typeof(WInputWithLabel) },
            { UIPrefabId.WTabPanelItem_InputRebind, typeof(WInputRebind) },
            { UIPrefabId.WTabPanelItem_ProgressButtonSmall, typeof(WProgressWithButton) },
            { UIPrefabId.WTabPanelItem_Slider, typeof(WSliderWithLabel) },
            { UIPrefabId.WTabPanelItem_Slider_Range, typeof(WSliderRangeWithLabel) },
            { UIPrefabId.WTabPanelItem_Text2, typeof(WText2) },
            { UIPrefabId.WTabPanelItem_Text2Button, typeof(WText2WithButton) },
            { UIPrefabId.WTabPanelItem_TextBlock, typeof(WText) },
            { UIPrefabId.WTabPanelItem_Toggle, typeof(WToggleText) },
            { UIPrefabId.WTabPanelItem_Toggle2, typeof(WToggle2) },
            { UIPrefabId.WTabPanelItem_ToggleButtonSmall, typeof(WToggleWithButton) },
            { UIPrefabId.WTabPanelItem2_Button, typeof(WButtonText) },
            { UIPrefabId.WTabPanelItem2_Dropdown, typeof(WDropdownWithLabel) },
            { UIPrefabId.WText_QuickSearchNoResults, typeof(WText) },
            { UIPrefabId.WTextCentered_EntityPanelFooterHack, typeof(WText) },
            { UIPrefabId.WUpgradeInstallButton, typeof(WButtonIconText) },
            { UIPrefabId.WUpgradeItem, typeof(WButtonIconText2) },
            { UIPrefabId.WJobMatrixHeaderButton, typeof(WButtonIconText) },
            { UIPrefabId.WJobMatrixRow, typeof(WJobMatrixRow) },
            { UIPrefabId.WJobMatrixToggle, typeof(WJobMatrixToggle) },
            { UIPrefabId.WJobMatrixTogglePrio, typeof(WJobMatrixPriority) },
        };

        public static readonly IReadOnlyDictionary<string, Type[]> AlternativeTypesByPrefabId =
            new Dictionary<string, Type[]> {
                { UIPrefabId.WContent2ListButton, new[] { typeof(WButtonIconText2), typeof(WContentListButton) } },
                { UIPrefabId.WContent2Text, new[] { typeof(WIconText), typeof(WIconText2) } },
                { UIPrefabId.WContentListButton, new[] { typeof(WButtonIconText2), typeof(WContentListButton) } },
            };

        public static Type TypeFor(string prefabId) {
            TypeByPrefabId.TryGetValue(prefabId, out var type);
            return type;
        }

        public static bool HasType(string prefabId, Type type) {
            if (TypeByPrefabId.TryGetValue(prefabId, out var canonicalType) && canonicalType == type) {
                return true;
            }
            if (AlternativeTypesByPrefabId.TryGetValue(prefabId, out var alternatives)) {
                for (int i = 0; i < alternatives.Length; i++) {
                    if (alternatives[i] == type) {
                        return true;
                    }
                }
            }
            return false;
        }

        public static WButtonText ExampleSpawnSimpleButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WButtonText>(life, UIPrefabId.WPanelButton, parent, siblingIdx);
        }

        public static WText ExampleSpawnText(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WText>(life, UIPrefabId.WContentHeader, parent, siblingIdx);
        }

        public static WButtonIconText ExampleSpawnIconButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WButtonIconText>(life, UIPrefabId.WPanelButtonIcon, parent, siblingIdx);
        }

        public static WButtonIconText2 ExampleSpawnIconButton2(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, parent, siblingIdx);
        }

        public static WToggleText ExampleSpawnToggle(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WToggleText>(life, UIPrefabId.WContent2Toggle, parent, siblingIdx);
        }

        public static WDropdownWithIcon ExampleSpawnDropdownWithIcon(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WDropdownWithIcon>(life, UIPrefabId.WContent2Dropdown, parent, siblingIdx);
        }

        public static WDropdownWithLabel ExampleSpawnDropdownWithLabel(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WDropdownWithLabel>(life, UIPrefabId.WTabPanelItem_Dropdown, parent, siblingIdx);
        }

        public static WSliderWithLabel ExampleSpawnSlider(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WSliderWithLabel>(life, UIPrefabId.WFooterItem_Slider, parent, siblingIdx);
        }

        public static WSliderRangeWithLabel ExampleSpawnSliderRange(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WSliderRangeWithLabel>(life, UIPrefabId.WTabPanelItem_Slider_Range, parent, siblingIdx);
        }

        // Dark-background row with title + subtitle text, a numeric amount input, and a toggle
        // (WContent2Text2AmtToggle). Texts.Load(title, subtitle) sets both text lines; Toggle enables
        // or disables the item (update Texts to reflect enabled/disabled state, e.g. Texts.Strike).
        // Amt holds the numeric value. Call Resolver.AutoResize() after loading to prevent overlap.
        public static WInputAmtText2Toggle ExampleSpawnText2AmtToggle(Lifetime life,
                RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WInputAmtText2Toggle>(life,
                UIPrefabId.WContent2Text2AmtToggle, parent, siblingIdx);
            var enabled = true;
            w.Texts.Load("Command Name", "Rep Cost:");
            w.Toggle.Load(enabled, isOn => {
                enabled = isOn;
                w.Texts.SetText(isOn ? "Command Name" : Texts.Strike("Command Name"));
            });
            w.Amt.Load(100, v => { /* apply new value */ }, v => Maths.Clamp(v, 0, 999999));
            w.Resolver.AutoResize();
            return w;
        }

        // Dark-background slider row with icon, title, and subtitle (WContent2Text2Slider).
        // IMPORTANT: Do NOT set any text inside the Slider.Load onChange callback.
        // Instead, always use Slider.SetValueTextFunc to control the displayed value text.
        // This keeps the slider's value label in sync during drag, not only on commit.
        //
        // BAD — do NOT do this (text is only updated on commit, not during drag):
        //   sliderRow.Slider.Load(currentValue, v => {
        //       sliderRow.Item.Load(IconId.CTemperature, "Temperature", Texts.Temperature(v));
        //   }, min, max);
        //
        // GOOD — value text is always current, onChange is only for side-effects:
        public static WSliderWithIconText2 ExampleSpawnDarkSliderWithIcon(Lifetime life,
                RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WSliderWithIconText2>(life,
                UIPrefabId.WContent2Text2Slider, parent, siblingIdx);
            w.Item.Load(IconId.CTemperature, "Temperature", "20 °C");
            w.Slider.Load(293f, v => { /* apply value, e.g. heater.Limit = Mathf.RoundToInt(v) */ }, 243f, 373f);
            w.Slider.SetValueTextFunc(v => Texts.Temperature(Mathf.RoundToInt(v)));
            return w;
        }

        // QuickChoice popup with a slider option.
        // ValueToText is the ONLY way to set Text2 on the slider row — do NOT call SetText2 inside OnValueChanged.
        // The popup auto-widens to at least 350 px when a slider is present.
        //
        // BAD — do NOT set text inside OnValueChanged (only fires on commit, not during drag):
        //   qc.AddSlider("Volume", new QuickChoice.Slider() {
        //       Value = 0.5f, Min = 0f, Max = 1f,
        //       OnValueChanged = v => { row.Item.SetText2($"{v:P0}"); },  // wrong
        //       ValueToText = v => $"{v:P0}",
        //   });
        //
        // GOOD — ValueToText drives the label; OnValueChanged is for side-effects only:
        public static void ExampleQuickChoiceWithSlider(RectTransform anchor) {
            var qc = QuickChoice.Create("Settings");
            qc.Widget = anchor;
            qc.AddSlider("Volume", new QuickChoice.Slider() {
                Value = 0.5f,
                Min = 0f,
                Max = 1f,
                Icon = Images.Sprite(IconId.CSoundOn),
                ValueToText = v => $"{Mathf.RoundToInt(v * 100)} %",
                OnValueChanged = v => { /* apply value, e.g. The.Audio.SetVolume(v) */ },
            });
            qc.Show();
        }

        public static WIconText2Progress ExampleSpawnProgress(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContentProgress, parent, siblingIdx);
        }

        public static WProgressWithLabel ExampleSpawnProgressHeader(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WProgressWithLabel>(life, UIPrefabId.WContentHeaderProgress, parent, siblingIdx);
        }

        public static WQuickSearchResult ExampleSpawnQuickSearchResult(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, parent, siblingIdx);
        }

        public static WQuickSearchPinnedItem ExampleSpawnQuickSearchPin(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WQuickSearchPinnedItem>(life, UIPrefabId.WQuickSearchButton, parent, siblingIdx);
        }

        // Dark-background button with icon + title + subtitle (Item), plus a small action button on the right (Button).
        // Typical pattern: Item click navigates/selects the entity; Button performs a quick action (e.g. turn off/on).
        public static WContentButton2Text2 ExampleSpawnButton2Text2(Lifetime life, RectTransform parent,
                int siblingIdx = -1) {
            var w = UISpawn.Widget<WContentButton2Text2>(life,
                UIPrefabId.WContent2Button2Text2, parent, siblingIdx);
            w.Item.Load(IconId.CInfo, "Device Name", "100 W", () => { /* select or focus entity */ });
            w.Button.Load("Off", () => { /* toggle device off/on */ });
            return w;
        }

        // Dark-background button with icon + title (Button), plus a small X dismiss button on the right (XButton).
        // Typical pattern: Button opens a detail popup for the item; XButton cancels/removes it.
        // Call row.Dispose() from XButton's callback to remove the row without rebuilding the whole list.
        public static WContentButtonWithX ExampleSpawnButtonWithX(Lifetime life, RectTransform parent,
                int siblingIdx = -1) {
            var w = UISpawn.Widget<WContentButtonWithX>(life,
                UIPrefabId.WContent2ButtonX, parent, siblingIdx);
            w.Button.Load(IconId.CInfo, "Item Name", () => { /* open detail popup */ });
            w.XButton.Load(() => {
                /* cancel/remove action */
                w.Dispose();
            });
            return w;
        }

        public static WContentBlock ExampleSpawnBlock(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WContentBlock>(life, UIPrefabId.WContentBlock, parent, siblingIdx);
        }

        // --- Content blocks inside WGroup (used in BaseEntityTab.LoadContent / EntityOverviewBlock) ---

        // Creates a content block inside a WGroup. This is the standard pattern for
        // BaseEntityTab.LoadContent and EntityOverviewBlock.LoadContent:
        //   var block = UISpawn.Block(lifetime, group, groupIdx++);
        //   int idx = 0;
        //   // spawn widgets into block.Container using idx++
        public static WContentBlock ExampleSpawnBlockInGroup(Lifetime life, WGroup group, int groupIdx) {
            return UISpawn.Block(life, group, groupIdx);
        }

        // --- Section headers ---

        // Dark-background section header (use inside WContent2* blocks)
        public static WText ExampleSpawnDarkHeader(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, parent, siblingIdx);
            w.Load("Section Title");
            return w;
        }

        // Dark-background header with a clickable button on the right
        public static WButtonText2 ExampleSpawnDarkHeaderButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonText2>(life, UIPrefabId.WContent2HeaderButton, parent, siblingIdx);
            w.Load("Header", () => { /* on click */ }, "Action");
            return w;
        }

        // Light-background section header
        public static WText ExampleSpawnLightHeader(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WText>(life, UIPrefabId.WContentHeader, parent, siblingIdx);
            w.Load("Section Title");
            return w;
        }

        // --- Info text (single line) ---

        // Dark-background info row with icon + text
        public static WIconText ExampleSpawnDarkInfoText(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, parent, siblingIdx);
            w.Load(IconId.CInfo, "Status message");
            return w;
        }

        // Light-background info row with icon + text (used in overview blocks)
        public static WIconText ExampleSpawnLightInfoText(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, parent, siblingIdx);
            w.Load(IconId.CInfo, "Status message");
            return w;
        }

        // --- Info text (two lines: icon + title + subtitle) ---

        // Dark-background two-line info row
        public static WIconText2 ExampleSpawnDarkInfoText2(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContent2Text2, parent, siblingIdx);
            w.Load(IconId.CInfo, "Title", "Subtitle");
            return w;
        }

        // Light-background two-line info row (used in overview blocks for status + detail)
        public static WIconText2 ExampleSpawnLightInfoText2(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, parent, siblingIdx);
            w.Load(IconId.CInfo, "Title", "Subtitle");
            return w;
        }

        // --- Multi-line text block ---

        // Dark-background multi-line text block for longer descriptions
        public static WText ExampleSpawnDarkTextBlock(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, parent, siblingIdx);
            w.Load("Longer paragraph of text that may wrap to multiple lines.");
            return w;
        }

        // --- Being avatar button ---

        // Button with a being's avatar image. Use WContent2Button_Being prefab,
        // then call beingData.Avatar.Load(w.Image) to load the avatar asynchronously.
        // Do NOT use BeingAvatar.Sprite directly — it is obsolete.
        public static WButtonIconText2 ExampleSpawnBeingButton(Lifetime life, RectTransform parent,
                BeingData beingData, Action onClick, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, parent, siblingIdx);
            w.Load(null, beingData.Name, "Action", onClick);
            beingData.Avatar.Load(w.Image);
            UIUtils.AddTipFunc(life, w, () => beingData.Name);
            return w;
        }

        // Being row with a big avatar + decorated name (Item, a WBeingListItem that selects
        // the being on click and keeps its name current), a side text and two icon buttons.
        // Call UICollisionResolver.AutoResize() after changing TextSide.
        // See CombatControlsRow for a real-world usage.
        public static WBeingButton2 ExampleSpawnBeingButton2(Lifetime life, RectTransform parent,
                BeingData beingData, int siblingIdx = -1) {
            var w = UISpawn.Widget<WBeingButton2>(life,
                UIPrefabId.WContent2Button_Being_Button2, parent, siblingIdx);
            w.Item.Load(beingData);
            w.TextSide.Load("12");
            w.UICollisionResolver.AutoResize();
            w.IconButton1.Load(IconId.CDraft, () => { /* first quick action */ });
            w.IconButton2.Load(IconId.CCombat, () => { /* second quick action */ });
            return w;
        }

        // --- Simple buttons ---

        // Dark-background simple button (no icon, just text)
        public static WButtonText ExampleSpawnDarkSimpleButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, parent, siblingIdx);
            w.Load("Click me", () => { /* on click */ });
            return w;
        }

        // --- Button with icon, two text lines, and a trailing label ---

        // Dark-background button with icon + title + subtitle, plus a small label on the right
        // (e.g. a price-change badge). Button sub-widget handles the click; Label shows extra text.
        public static WButtonIconText2WithLabel ExampleSpawnButtonIconText2WithLabel(Lifetime life,
                RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonIconText2WithLabel>(life,
                UIPrefabId.WContent2ButtonText3, parent, siblingIdx);
            w.Button.Load(IconId.CInfo, "Title", "Subtitle", () => { /* on click */ });
            w.Label.Load("Label");
            return w;
        }

        // Dark-background button with a checkbox in place of the icon (e.g. codex pages that can be
        // marked as learned). Two click targets: the checkbox cell toggles, the rest of the row
        // clicks Button. Pass a null icon - the prefab has no icon.
        public static WButtonIconText2WithToggle ExampleSpawnButtonIconText2WithToggle(Lifetime life,
                RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonIconText2WithToggle>(life,
                UIPrefabId.WContent2ButtonToggle, parent, siblingIdx);
            w.Button.Load(null, "Title", null, () => { /* open the item */ });
            w.Toggle.Load(true, isOn => { /* mark the item */ });
            return w;
        }

        // --- Toggle with icon and two text lines ---

        // Dark-background toggle with icon, title, and state text.
        // WToggleWithIconText2 has sub-widgets: Item (WIconText2) and Toggle (WToggle).
        public static WToggleWithIconText2 ExampleSpawnDarkToggleWithIcon(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WToggleWithIconText2>(life, UIPrefabId.WContent2Text2Toggle, parent, siblingIdx);
            w.Item.Load(IconId.CCheck, "Feature Name", "Enabled");
            w.Toggle.Load(true, isOn => { /* on toggle */ });
            return w;
        }

        // --- Spacer ---

        public static WSpacer ExampleSpawnSpacer(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WSpacer>(life, UIPrefabId.WSpacer, parent, siblingIdx);
        }

        // --- Footer widgets (used in BaseEntityTab.LoadFooter) ---

        // Footer toggle button
        public static WToggleText ExampleSpawnFooterToggle(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WToggleText>(life, UIPrefabId.WFooterItem_Toggle, parent, siblingIdx);
            w.Load("Auto Mode", true, isOn => { /* on toggle */ });
            return w;
        }

        // Footer confirm button (highlighted style)
        public static WButtonText ExampleSpawnFooterConfirmButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonText>(life, UIPrefabId.WPanelButton_Confirm, parent, siblingIdx);
            w.Load("Confirm", () => { /* on click */ });
            return w;
        }

        // Footer danger/red button
        public static WButtonText ExampleSpawnFooterRedButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WButtonText>(life, UIPrefabId.WPanelButton_Red, parent, siblingIdx);
            w.Load("Delete", () => { /* on click */ });
            return w;
        }

        // Sub-panel footer toggle (used for per-item settings in SubPanelWidget).
        // Important: when used inside an already open sub-panel, avoid calling ReloadTab()
        // from the toggle callback unless a full rebuild is required. Full rebuilds can
        // re-open and reposition the sub-panel, causing visible jump.
        public static WToggleText ExampleSpawnSubPanelFooterToggle(Lifetime life,
                SubPanelWidget subPanel, string label, bool value,
                Action<bool> onToggle, int siblingIdx = -1) {
            var w = UISpawn.Widget<WToggleText>(life,
                UIPrefabId.WFooterItem_Toggle, subPanel.Footer.Container, siblingIdx);
            w.Load(label, value, isOn => {
                onToggle?.Invoke(isOn);
            });
            subPanel.ShowFooter(true);
            return w;
        }

        // --- Progress bars ---

        // Dark-background progress bar with icon and two text lines
        public static WIconText2Progress ExampleSpawnDarkProgress(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WIconText2Progress>(life, UIPrefabId.WContent2Progress, parent, siblingIdx);
        }

        // Dark-background progress bar with a clickable button
        public static WButtonIconText2Progress ExampleSpawnDarkProgressButton(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WButtonIconText2Progress>(life, UIPrefabId.WContent2ProgressButton, parent, siblingIdx);
        }

        // Green-tinted header progress bar
        public static WProgressWithLabel ExampleSpawnGreenProgressHeader(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WProgressWithLabel>(life, UIPrefabId.WContentHeaderProgress_Green, parent, siblingIdx);
        }

        // --- Grid block ---

        // Content block with grid layout (items arranged in a grid instead of vertical list)
        public static WContentBlock ExampleSpawnGridBlock(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WContentBlock>(life, UIPrefabId.WContentBlock_Grid, parent, siblingIdx);
        }

        // IMPORTANT — IStatProvider.AddStats constraints:
        // 1. Never use WContent2* prefabs — always use WContent* (light) variants.
        // 2. Never spawn section headers.
        // 3. Use WContentListButton (not WContent2ListButton) for expandable sections.
        public static WContentListButton ExampleSpawnContentListButton(Lifetime life,
                RectTransform parent, string icon, string title, string text2,
                WContentListButton.OnToggleDelegate onToggle, bool expanded = false,
                int siblingIdx = -1) {
            var listButton = UISpawn.Widget<WContentListButton>(life,
                UIPrefabId.WContentListButton, parent, siblingIdx);
            listButton.Load(icon, title, text2);
            listButton.LoadOnToggle(life, onToggle, expanded);
            // If a tooltip needs to be added on an expandable button, use this:
            UIUtils.AddTip(life, listButton.PopupArea, "Tip");
            // Or for codex (set actual def instead of null)
            UIUtils.AddCodexTooltip(life, listButton.PopupArea.gameObject, listButton, (Def) null);
            return listButton;
        }

        public static bool ExampleContentListOnToggle(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded) {
            if (!willShow) {
                return wasLoaded;
            }

            // in more complex cases, when internal items have tooltips and bindings,
            // you may want to create and check a sub lifetime outside this method
            // and dispose it before recreating it along with the new items.
            // int this example, we will return to prevent spawning a duplicate
            // since we don't destroy the items when contracting the widget
            if (wasLoaded) { return true; }

            int idx = 0;
            var row0 = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, idx++);
            row0.Load(IconId.CInfo, "Example item A");
            var row1 = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, idx++);
            row1.Load(IconId.CInfo, "Example item B");
            return true;
        }

        public static WContentListButton ExampleSpawnContentListButtonWithExampleToggle(Lifetime life,
                RectTransform parent, bool expanded = false, int siblingIdx = -1) {
            return ExampleSpawnContentListButton(life, parent, IconId.CInfo,
                "Example expandable", "2", ExampleContentListOnToggle, expanded, siblingIdx);
        }

        // Light-background expandable section header with a trailing action button.
        // Backed by WContentListButtonWithButton (UIPrefabId.WContentHeaderExpandableButton).
        //
        // IMPORTANT — this prefab has NO icon on the WContentListButton side:
        //   w.Item.Load(text, text2)   ← correct (no-icon overload; pass null for text2 if unused)
        //   w.Item.Load(icon, text, text2)  ← will throw at runtime (image component not set up)
        //
        // w.Item  — WContentListButton: the clickable expand/collapse header row
        // w.Button — WButtonText: the small action button on the right (e.g. Rename, Edit)
        //
        // To persist expanded state across tab reloads, store it in an instance field of the
        // enclosing EntityOverviewBlock (singleton) and update it inside the LoadOnToggle callback.
        public static WContentListButtonWithButton ExampleSpawnExpandableHeaderWithButton(Lifetime life,
                RectTransform parent, bool expanded = false, int siblingIdx = -1) {
            var w = UISpawn.Widget<WContentListButtonWithButton>(life,
                UIPrefabId.WContentHeaderExpandableButton, parent, siblingIdx);
            w.Item.Load("Section Title", null);
            w.Button.Load("Action", () => { /* on button click */ });
            w.Item.LoadOnToggle(life, (toggleLife, container, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int cIdx = 0;
                var row = UISpawn.Widget<WIconText>(toggleLife, UIPrefabId.WContentText, container, cIdx++);
                row.Load(IconId.CInfo, "Example item");
                return true;
            }, expanded);
            return w;
        }

        public static WPanel ExampleSpawnPanel(Lifetime life, RectTransform parent, int siblingIdx = -1) {
            return UISpawn.Widget<WPanel>(life, UIPrefabId.WListChoicePopup, parent, siblingIdx);
        }

        public static WEntityDetailsPopup ExampleSpawnEntityDetailsPopup(Lifetime life,
                BeingData being, RectTransform parent, int siblingIdx = -1) {
            var w = UISpawn.Widget<WEntityDetailsPopup>(life,
                UIPrefabId.WEntityDetailsPopup, parent, siblingIdx);
            w.Load(being);
            return w;
        }

        public static WButtonIconText ExampleSpawnPermanentMainMenuButton(RectTransform parent) {
            return UISpawn.WidgetPermanent<WButtonIconText>(UIPrefabId.WMainMenuSideButton, parent);
        }

        public static Tooltip ExampleAddTip(Lifetime life, WBase widget, string tip) {
            return UIUtils.AddTip(life, widget, tip);
        }

        public static Tooltip ExampleAddDynamicTip(Lifetime life, WBase widget, Func<string> tipFactory) {
            return UIUtils.AddTipFunc(life, widget, tipFactory);
        }

        // AddHover fires a callback when the widget is hovered or unhovered.
        // Use the `on` flag to react only to enter (on=true) or exit (on=false).
        // Common pattern: update a shared "hovered item" reference and re-render
        // a dependent view (e.g. a chart) from the enter callback only.
        // Example from WalletTab_StockMarket — hovering a stock item switches
        // the chart panel to show that item's price history:
        //
        //   UIUtils.AddHover(lifetime, itemWidget, on => {
        //       if (on) {
        //           hoveredItem = capturedItem;
        //           parent.RenderStockChart(capturedItem);
        //       }
        //   });
        public static void ExampleAddHover(Lifetime life, WBase widget) {
            UIUtils.AddHover(life, widget, on => {
                if (on) {
                    The.SysSig.ShowTooltip.Send("Hovered", true);
                } else {
                    The.SysSig.HideTooltip.Send();
                }
            });
        }

        public static void ExampleAddTooltipDynamic(GameObject target, Func<string> tooltipFactory) {
            UIUtils.AddTooltipDynamic(target, tooltipFactory);
        }

        public static void ExampleAddCodexTooltip(Lifetime life, WBase widget, Def def,
                bool optional = true, float showDelay = 0f) {
            UIUtils.AddCodexTooltip(life, widget.gameObject, widget, def, optional, showDelay);
        }

        public static void ExampleRemoveCodexTooltip(GameObject target) {
            UIUtils.RemoveCodexTooltip(target);
        }

        // --- Sub-panel re-open after tab reload ---

        // When LoadContent detects that a sub-panel was open before the reload
        // (via SubPanelWidget.IsOpenFor), the anchor widget has just been
        // spawned from the pool and Unity layout has NOT run yet.
        // Rect.GetWorldCorners() would return stale positions, causing the
        // sub-panel to jump to the wrong spot.
        //
        // Fix: call LayoutRebuilder.ForceRebuildLayoutImmediate on the
        // container BEFORE calling subPanel.Show, then schedule a safety-net
        // reposition with connectorOnly: false on the next frame.
        //
        // BAD  — do NOT do this (sub-panel will appear at wrong position):
        //   if (SubPanelWidget.IsOpenFor == SubId && contentBtn != null) {
        //       ShowMySubPanel(contentBtn);  // widget not laid out yet!
        //   }
        //
        // GOOD — force layout first, deferred full reposition as safety net:
        public static void ExampleSubPanelReopenOnReload(
                GameState s, Lifetime lifetime, WGroup group,
                WBase contentBtn, string subId, Action<WBase> showSubPanel) {
            if (SubPanelWidget.IsOpenFor == subId && contentBtn != null) {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(
                    group.Container);
                showSubPanel(contentBtn);
                var btn = contentBtn;
                Coroutines.OnNextFrame(() => {
                    var sp = s.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(btn, connectorOnly: false);
                });
            }
        }

        public static void ExampleAddDropdownTooltips(WDropdown dropdown, string[] tooltips, bool translate = false) {
            UIUtils.AddTooltip(dropdown, tooltips, translate);
        }

        public static void ExampleLoadSearch(WInput searchInput, Action<string> onUpdate,
                Action<string> onSubmit = null, string placeholder = null, string value = null) {
            UIUtils.LoadSearch(searchInput, onUpdate, onSubmit, placeholder, value);
        }

        // --- Paginated list ("load more" pattern) ---
        //
        // Use this pattern when a block may contain hundreds or thousands of items and spawning
        // all widgets at once would be too slow. The first PageSize entries are spawned immediately;
        // a button labelled "+N" is appended after them. Each click inserts the next batch of
        // up to PageSize widgets directly before the button (via sibling index) and moves the
        // button past them, so no layout is rebuilt from scratch and the scroll position is preserved.
        //
        // Key rules:
        //   1. Collect all entries into a List<T> BEFORE spawning anything — the list is captured
        //      by the button's closure and reused across multiple clicks.
        //   2. The button belongs to the enclosing Lifetime; use SetActiveFast(false) to dismiss
        //      it when the list is exhausted instead of destroying it manually.
        //   3. Re-call btn.Load(text, action) to update both label and handler in one step —
        //      no per-button sub-lifetime is needed.
        //   4. After each click, rebuild layout WITHOUT resetting scroll position so the user
        //      stays where they are while new content appears below.
        //   5. Only the initial full render scrolls to top.
        //
        // See: LogHandler.RefreshContent / LoadMoreAction for a real-world usage.
        private const int ExamplePageSize = 100;

        public static void ExampleSpawnPaginatedTextList(
                Lifetime life, RectTransform container,
                UnityEngine.UI.ScrollRect scrollRect,
                List<string> allEntries) {
            int loadCount = Math.Min(ExamplePageSize, allEntries.Count);
            for (int i = 0; i < loadCount; i++) {
                var row = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, container, i);
                row.Load(allEntries[i]);
            }
            if (allEntries.Count > ExamplePageSize) {
                var btn = UISpawn.Widget<WButtonText>(life,
                    UIPrefabId.WContent2ButtonSimple, container, loadCount);
                ExamplePaginatedLoadMoreAction(btn, life, container, scrollRect, allEntries, loadCount);
            }
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private static void ExamplePaginatedLoadMoreAction(
                WButtonText btn, Lifetime life, RectTransform container,
                UnityEngine.UI.ScrollRect scrollRect,
                List<string> entries, int loadedCount) {
            btn.Load(Texts.Italic($"+{entries.Count - loadedCount}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(ExamplePageSize, entries.Count - loadedCount);
                for (int i = 0; i < batch; i++) {
                    var row = UISpawn.Widget<WText>(life,
                        UIPrefabId.WContent2TextBlock, container, sibIdx + i);
                    row.Load(entries[loadedCount + i]);
                }
                int newLoaded = loadedCount + batch;
                if (newLoaded < entries.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    ExamplePaginatedLoadMoreAction(btn, life, container, scrollRect, entries, newLoaded);
                } else {
                    btn.gameObject.SetActiveFast(false);
                }
                // Rebuild both content and viewport without scrolling to top.
                // Both are required: content resizes to fit new items, viewport updates its mask.
                // Skipping either causes the container to remain at its pre-expansion size.
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.viewport);
            });
        }
    }
}
