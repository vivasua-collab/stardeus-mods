using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.UI;
using Game.Utils;
using KL.Utils;
using KL.UweenExt;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WResearchNode : WBase, IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private WText unlocksText;
        [SerializeField] private WText requiresText;
        [SerializeField] private WImage bookmarkIcon;
        [SerializeField] private WImage unlockPrefab;
        [SerializeField] private WText unlockExtraPrefab;
        [SerializeField] private WResearchReq requirePrefab;
        [SerializeField] private UIClickableHoverable bookmarkButton;
        [SerializeField] private GameObject unlocksBlock;
        [SerializeField] private GameObject outline;
        [SerializeField] private CanvasGroup outlineCG;
        [SerializeField] private WGroup unlocksContainer;
        [SerializeField] private GameObject requiresBlock;
        [SerializeField] private WGroup requiresContainer;
        [SerializeField] private GameObject unlockButtonBlock;
        [SerializeField] private WButtonText unlockButton;
        [SerializeField] private WIconText unlockedInfo;
        [SerializeField] private WText notDiscoveredInfo;
        [SerializeField] private LayoutGroup[] layoutGroups;
        [SerializeField] private ContentSizeFitter[] sizeFitters;
        private readonly List<Graphic> itemRaycastables = new();
        private readonly Dictionary<Def, WImage> spawnedUnlockDefs = new();
        private readonly Dictionary<IResearchUnlockEffect, WImage> spawnedUnlockEffects = new();
        /// <summary>
        /// Int - def id (datoid id or mini disk module id)
        /// </summary>
        private readonly Dictionary<int, WResearchReq> spawnedRequirements = new();
        override protected bool IsPoolable => false;
        private WText extraUnlocksText;
        private Lifetime lifetime;
        private Lifetime tipLife;
        public CanvasGroup CanvasGroup;
        private Researchable researchable;
        private ResearchProgress progress;
        private WText placeholder;
        private bool lastTextStaticState;
        private bool isMoving;
        private bool hasPointer;
        private bool isLackingDiskOrDatoids;
        public static Color ColorAvailable;
        public static Color ColorLocked;
        public static Color ColorUnlocked;
        public static Color ColorNotDiscovered;
        private string bigTextUnlocked;
        private string bigTextAvailable;
        private string bigTextAvailableBookmarked;
        private string bigTextLocked;
        private string bigTextLockedBookmarked;
        private string bigTextUndiscovered;
        public bool DebugShowsNotDiscovered => notDiscoveredInfo.gameObject.activeSelf;
        public bool DebugShowsUnlocked => unlockedInfo.gameObject.activeSelf;
        public bool DebugShowsIcon => icon.gameObject.activeSelf;

        private void Awake() {
            requiresText.SetText(T.VerbResearchRequires);
            unlocksText.SetText(T.VerbResearchUnlocks);
            unlockedInfo.SetText(T.ResearchUnlocked);
            notDiscoveredInfo.SetText(T.UndiscoveredResearch);
        }

        public void Load(Researchable res, ResearchProgress prog, WText placeholder) {
            bigTextAvailable = Texts.Green(res.Def.NameT);
            bigTextAvailableBookmarked = Texts.Green(res.Def.NameT);
            bigTextUnlocked = res.Def.NameT;
            bigTextLocked = Texts.Grey(res.Def.NameT);
            bigTextLockedBookmarked = Texts.Grey(res.Def.NameT);
            bigTextUndiscovered = Texts.Grey(T.UndiscoveredResearch);
            outline.SetActiveFast(false);
            progress = prog;
            researchable = res;
            icon.sprite = res.Def.PreviewSprite;
            nameText.text = res.NameT;
            bookmarkButton.OnClick = ToggleBookmark;
            this.placeholder = placeholder;
            placeholder.SetText(res.Def.NameT, false);

            CreateUnlocks();
            CreateRequirements();

            ToggleItemRaycastables(false);
            UpdateResearchable();

            prog.ChangeCallback = UpdateResearchable;
        }

        private void ToggleBookmark(PointerEventData data) {
            if (progress.IsBookmarked) {
                A.S.Sys.Research.TryRemoveBookmark(progress, true);
            } else {
                A.S.Sys.Research.TryAddBookmark(progress);
            }
        }

        private void TryUnlock() {
            if (A.S.Research.TryUnlock(progress)) {
                isLackingDiskOrDatoids = false;
                A.S.Sys.Research.NotifyCompleted(progress);
                PlayStats.TrackResearchUnlocked(researchable.Def.Id);
            }
            UpdateResearchable();
        }

        public void Flash() {
            Coroutines.StartDirect(FlashOutline());
        }

        private IEnumerator FlashOutline() {
            outline.SetActiveFast(true);
            outlineCG.alpha = 0f;
            float fadeInDuration = 0.25f;
            float fadeOutDuration = 0.25f;
            int numFlashes = 2;
            var s = A.S;
            for (int i = 0; i < numFlashes; i++) {
                while (outlineCG.alpha < 1f) {
                    outlineCG.alpha += Time.unscaledDeltaTime / fadeInDuration;
                    yield return null;
                    if (!GameState.IsCurrent(s)) {
                        yield break;
                    }
                }
                while (outlineCG.alpha > 0f) {
                    outlineCG.alpha -= Time.unscaledDeltaTime / fadeOutDuration;
                    yield return null;
                    if (!GameState.IsCurrent(s)) {
                        yield break;
                    }
                }
            }
            outline.SetActiveFast(!progress.IsUnlocked && progress.IsBookmarked);
            outlineCG.alpha = 1f;
        }

        private void CreateRequirements() {
            var adjCost = researchable.AdjustedDatoidCosts(A.S.Difficulty);
            var numDatoids = adjCost?.Length ?? 0;
            for (int i = 0; i < numDatoids; i++) {
                var cost = adjCost[i];
                var req = UISpawn.WidgetPermanent(requirePrefab, requiresContainer.Rect);
                req.LoadDatoid(cost);
                spawnedRequirements[req.Def.IdH] = req;
                itemRaycastables.Add(req.GetComponent<Graphic>());
            }
            var disk = UISpawn.WidgetPermanent(requirePrefab, requiresContainer.Rect);
            disk.LoadDisk(researchable.Storage);
            spawnedRequirements[disk.Def.IdH] = disk;
            itemRaycastables.Add(disk.GetComponent<Graphic>());
        }

        private void CreateUnlocks() {
            var allUnlockables = A.S.Research.GetUnlockableIconsFor(researchable.Def);
            var hideContents = !researchable.Def.IsInSeason;
            // two rows is max
            var addMoreBtn = allUnlockables.Count > 12;
            int cnt = 0;
            itemRaycastables.Add(unlocksText.GetComponent<Graphic>());
            foreach (var unl in allUnlockables) {
                cnt++;
                if (cnt > 11 && addMoreBtn) {
                    extraUnlocksText = UISpawn.WidgetPermanent(unlockExtraPrefab, unlocksContainer.Rect);
                    extraUnlocksText.SetText($"+{allUnlockables.Count - 11}");
                    itemRaycastables.Add(extraUnlocksText.GetComponent<Graphic>());
                    break;
                }
                var unlock = UISpawn.WidgetPermanent(unlockPrefab, unlocksContainer.Rect);
                itemRaycastables.Add(unlock.GetComponent<Graphic>());
                if (hideContents) {
                    unlock.SetSprite(Images.Sprite(IconId.CQuestion));
                    continue;
                }
                if (unl.def != null) {
                    unlock.SetSprite(unl.def.PreviewSprite);
                    spawnedUnlockDefs[unl.def] = unlock;
                } else if (unl.unlockEff != null) {
                    unlock.SetImage(unl.unlockEff.Icon);
                    spawnedUnlockEffects[unl.unlockEff] = unlock;
                } else {
                    D.Err("Neither unlockable def nor effect is set for research unlock! {0}",
                        researchable.Def.Id);
                }
            }
        }

        private void DisableLayoutGroups() {
            ToggleLayoutGroups(false);
        }

        public void ToggleLayoutGroups(bool on) {
            foreach (var g in sizeFitters) {
                g.enabled = on;
            }
            foreach (var g in layoutGroups) {
                g.enabled = on;
            }
        }

        public void OnMoveStarted() {
            // ToggleLayoutGroups(false);
            background.raycastTarget = false;
            if (lastTextStaticState) { return; }
            lastTextStaticState = true;
            placeholder.Text.isTextObjectScaleStatic = true;
            nameText.isTextObjectScaleStatic = true;
            unlockButton.Text.isTextObjectScaleStatic = true;
            unlocksText.Text.isTextObjectScaleStatic = true;
            requiresText.Text.isTextObjectScaleStatic = true;
            unlockedInfo.Text.isTextObjectScaleStatic = true;
            foreach (WIconText req in spawnedRequirements.Values) {
                req.Text.isTextObjectScaleStatic = true;
            }
        }

        public void OnMoveEnded(bool scaleIncreased) {
            // ToggleLayoutGroups(true);
            background.raycastTarget = true;
            // if scale increased, we want to set lasttextstatic to off
            var isOn = !scaleIncreased;
            if (lastTextStaticState == isOn) { return; }
            lastTextStaticState = isOn;
            placeholder.Text.isTextObjectScaleStatic = isOn;
            nameText.isTextObjectScaleStatic = isOn;
            unlockButton.Text.isTextObjectScaleStatic = isOn;
            requiresText.Text.isTextObjectScaleStatic = isOn;
            unlocksText.Text.isTextObjectScaleStatic = isOn;
            unlockedInfo.Text.isTextObjectScaleStatic = isOn;
            foreach (WIconText req in spawnedRequirements.Values) {
                req.Text.isTextObjectScaleStatic = isOn;
            }
        }

        public void OnShowResearch(bool show) {
            background.raycastTarget = show;
            if (!show) {
                if (hasPointer) {
                    OnPointerExit(null);
                } else {
                    ToggleItemRaycastables(false);
                }
            }
            ToggleLayoutGroups(show);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (isMoving) { return; }
            if (hasPointer) { return; }
            hasPointer = true;
            ToggleItemRaycastables(true);
            AddTooltips();
        }

        private void AddTooltips() {
            if (tipLife != null) {
                tipLife.Dispose();
            }
            tipLife = lifetime.CreateChild("NodeTip");
            UIUtils.AddTipFunc(tipLife, unlocksText, AllUnlocksTip);
            foreach (var unl in spawnedUnlockDefs) {
                var def = unl.Key;
                var widget = unl.Value;
                UIUtils.AddCodexTooltip(tipLife, widget.gameObject, this, def,
                    optional: false, showDelay: Tooltip.DelaySeconds);
            }
            if (extraUnlocksText != null) {
                UIUtils.AddTipFunc(tipLife, extraUnlocksText, AllUnlocksTip);
            }
            foreach (var eff in spawnedUnlockEffects) {
                var unlockEff = eff.Key;
                var widget = eff.Value;
                UIUtils.AddTip(tipLife, widget, $"{Texts.Title(unlockEff.Title)}{unlockEff.Tooltip}");
            }
            if (progress.IsUnlocked) { return; }
            foreach (var req in spawnedRequirements.Values) {
                UIUtils.AddCodexTooltip(tipLife, req.gameObject, this, req.Def,
                    optional: false, showDelay: Tooltip.DelaySeconds);
                if (req.IsDatoid) {
                    UIUtils.AddTipFunc(tipLife, req, () => DatoidTipFor(req.Datoid));
                } else { // storage
                    UIUtils.AddTipFunc(tipLife, req, StorageTip);
                }
            }
            UIUtils.AddTipFunc(tipLife, bookmarkIcon, BookmarkTip);
            UIUtils.AddTipFunc(tipLife, unlockButton, UnlockButtonTip);
        }

        private string AllUnlocksTip() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append(Texts.SubTitle(T.VerbResearchUnlocks));
            if (!researchable.Def.IsInSeason) {
                sb.Append(T.ResearchSeasonalLocked_V1.F(researchable.Def.Seasonal.RangeText));
                return sb.ToString();
            }
            var allUnlockables = A.S.Research.GetUnlockableIconsFor(researchable.Def);
            foreach (var unl in allUnlockables.Where(u => u.def != null).OrderBy(u => u.def.NameT)) {
                // if (spawnedUnlockDefs.ContainsKey(unl.def)) { continue; }
                sb.Append(unl.def.CodexLink).Append('\n');
            }
            return sb.ToString();
        }

        private string UnlockButtonTip() {
            if (!researchable.Def.IsInSeason) {
                return T.ResearchSeasonalLocked_V1.F(researchable.Def.Seasonal.RangeText);
            }
            var s = A.S;
            if (isLackingDiskOrDatoids) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                var storageDeficit = researchable.Storage -
                    (A.Grid.TotalDisk - s.Research.UsedDisk);
                if (storageDeficit > 0) {
                    sb.AppendLine(T.ResearchLacksResource_V2.F(
                        T.ComputerResourcesStorage,
                        T.StorageModuleAmount_V1.F(storageDeficit)));
                }
                if (researchable.DatoidCosts != null) {
                    var datoidCounts = s.Research.DatoidCounts;
                    foreach (Mat cost in researchable.AdjustedDatoidCosts(s.Difficulty)) {
                        var avail = 0;
                        if (datoidCounts.TryGetValue(cost.Type, out var available)) {
                            avail = available;
                        }
                        var deficit = cost.StackSize - avail;
                        if (deficit > 0) {
                            sb.AppendLine(T.ResearchLacksResource_V2.F(
                                cost.Type.NameT, deficit));
                            cost.Type.Def.AppendUnlockInstructions(sb, true, false);
                        }
                    }
                }
                return sb.ToString();
            }
            if (!researchable.CanBeResearchedNow) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                var tier = researchable.Def.Tier;
                if (!A.S.Research.IsTierUnlocked(tier, out int toUnlock)) {
                    sb.AppendLine(T.ResearchTierLocked_V1.F(tier));
                }
                if (!Researchable.IsEmptyPrereq(researchable)) {
                    foreach (var p in researchable.Prerequisites) {
                        var r = s.Research.Get(Hashes.S(p));
                        if (!r.IsUnlocked) {
                            sb.AppendLine(T.ResearchPrerequisitesMissing_V1.F(r.Def.NameT));
                        }
                    }
                }
                return sb.ToString();
            }
            return null;
        }

        public void UpdateResources(Dictionary<MatType, BInt> datoids, int storage) {
            var isComplete = progress.IsUnlocked;
            var numDatoids = progress.Researchable.DatoidCosts.Length;

            if (isComplete) {
                UpdateResearchable();
                return;
            }

            var can = researchable.CanBeResearchedNow;
            var disk = spawnedRequirements[DefIdsH.ObjectsDevicesDiskModuleMini];
            if (researchable.Storage > storage) {
                can = false;
                isLackingDiskOrDatoids = true;
                disk.SetLacking(true);
            } else {
                disk.SetLacking(false);
            }
            var rawDatoids = progress.Researchable.AdjustedDatoidCosts(A.S.Difficulty);
            for (int i = 0; i < numDatoids; i++) {
                // ensure datoid is deserialized
                var datoid = rawDatoids[i];
                if (!spawnedRequirements.TryGetValue(datoid.Type.Def.IdH, out var cd)) {
                    D.Err("Missing datoid requirement widget for {0}", datoid.Type.Def.Id);
                    continue;
                }
                if (datoids.TryGetValue(datoid.Type, out var available)) {
                    if (datoid.StackSize <= available) {
                        cd.SetLacking(false);
                    } else {
                        can = false;
                        isLackingDiskOrDatoids = true;
                        cd.SetLacking(true);
                    }
                } else {
                    cd.SetLacking(true);
                }
            }

            progress.CanBeUnlockedNow = can;

            UpdateResearchable();
        }

        public void UpdateResearchable() {
            if (!CheckDiscovered()) { return; }
            // D.Warn("Updating researchable node for {0}", researchable.Def.Id);

            var isLocked = !progress.IsUnlocked;
            bookmarkIcon.SetActiveFast(isLocked);
            requiresBlock.SetActiveFast(isLocked);
            unlockButtonBlock.SetActiveFast(isLocked);
            unlockedInfo.SetActiveFast(!isLocked);
            if (isLocked) {
                var isBookmarked = progress.IsBookmarked;
                var bookIcon = isBookmarked ? IconId.CBookmark : IconId.CBookmarkInactive;
                bookmarkIcon.Load(bookIcon);
                outline.SetActiveFast(isBookmarked);
                if (!researchable.Def.IsInSeason) {
                    placeholder.SetText(isBookmarked ? bigTextLockedBookmarked : bigTextLocked, false);
                    UpdateButton(false, T.ResearchUnlock);
                } else if (progress.CanBeUnlockedNow) {
                    UpdateButton(true, T.ResearchUnlock);
                    placeholder.SetText(isBookmarked ? bigTextAvailableBookmarked : bigTextAvailable, false);
                } else {
                    placeholder.SetText(isBookmarked ? bigTextLockedBookmarked : bigTextLocked, false);
                    UpdateButton(false, T.ResearchLocked);
                }
                background.color = BackgroundColorFor(progress, true);
            } else {
                placeholder.SetText(bigTextUnlocked, false);
                background.color = BackgroundColorFor(progress, true);
                outline.SetActiveFast(false);
                isLackingDiskOrDatoids = false;
                progress.CanBeUnlockedNow = false;
            }
        }

        // The one definition of a research's state color, shared by its card and its tile
        public static Color BackgroundColorFor(ResearchProgress prog, bool isDiscovered) {
            if (!isDiscovered) { return ColorNotDiscovered; }
            if (prog.IsUnlocked) { return ColorUnlocked; }
            if (!prog.Researchable.Def.IsInSeason) { return ColorLocked; }
            return prog.CanBeUnlockedNow ? ColorAvailable : ColorLocked;
        }

        // Node widgets live for the whole session and are never rebuilt, so this has to be a
        // pure function of the current state - a remembered "last known" flag here used to
        // leave nodes painted as both unlocked and undiscovered after they got discovered.
        private bool CheckDiscovered() {
            var yes = A.S.Research.IsDiscoveredOrSandbox(researchable.Def.IdH);
            if (!yes) {
                placeholder.SetText(bigTextUndiscovered, false);
                background.color = BackgroundColorFor(progress, false);
            }
            notDiscoveredInfo.SetActiveFast(!yes);
            bookmarkIcon.SetActiveFast(yes);
            unlocksBlock.SetActiveFast(yes);
            requiresBlock.SetActiveFast(yes);
            unlockButtonBlock.SetActiveFast(yes);
            unlockedInfo.SetActiveFast(yes);
            icon.SetActiveFast(yes);
            nameText.SetActiveFast(yes);
            return yes;
        }

        private void UpdateButton(bool clickable, string text) {
            unlockButton.Load(text, TryUnlock);
            unlockButton.SetInteractable(clickable);
        }

        private string BookmarkTip() {
            return progress.IsBookmarked ? T.BookmarkRemove : T.BookmarkAdd;
        }

        private string DatoidTipFor(Mat datoid) {
            var tip = T.RequiredMaterial_V1.F(Texts.Bold(Units.Num(datoid.StackSize)));
            tip = $"{Texts.SubTitle(datoid.Type.NameT)}{tip}";
            int avail = 0;
            if (A.S.Research.DatoidCounts.TryGetValue(datoid.Type, out var available)) {
                avail = available;
            }
            tip = $"{tip}\n{T.AvailableMaterial_V1.F(Texts.Bold(Units.Num(avail)))}";
            return tip;
        }

        private string StorageTip() {
            var avail = A.Grid.TotalDisk - A.S.Research.UsedDisk;
            if (avail < 0) { avail = 0; }
            var need = researchable.Storage;
            var tip = T.ResearchTipStorage;
            tip = $"{tip}{GlyphMap.BRHR}{T.RequiredMaterial_V1.F(Texts.Bold(T.StorageModuleAmount_V1.F(need)))}";
            tip = $"{tip}\n{T.AvailableMaterial_V1.F(Texts.Bold(T.StorageModuleAmount_V1.F(avail)))}";
            return tip;
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!hasPointer) { return; }
            hasPointer = false;
            ToggleItemRaycastables(false);
            RemoveTooltips();
        }

        private void RemoveTooltips() {
            foreach (var unl in spawnedUnlockDefs) {
                var widget = unl.Value;
                UIUtils.RemoveCodexTooltip(widget.gameObject);
            }
            if (tipLife != null) {
                tipLife.Dispose();
                tipLife = null;
            }
        }

        private void ToggleItemRaycastables(bool on) {
            bookmarkIcon.SetRaycastable(on);
            unlockButton.SetRaycastable(on);
            for (int i = itemRaycastables.Count - 1; i >= 0; i--) {
                Graphic ir = itemRaycastables[i];
                ir.raycastTarget = on;
            }
        }

        public void SetLifetime(Lifetime lifetime) {
            this.lifetime = lifetime;
        }
    }
}
