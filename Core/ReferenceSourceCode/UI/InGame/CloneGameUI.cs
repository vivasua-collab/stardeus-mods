using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Input;
using Game.Modifiers;
using Game.Procgen;
using Game.Scenarios;
using Game.Story;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Systems.Story;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.CodeGen;

namespace Game.UI {
    public sealed class CloneGameUI : IContextPopup {
        public int UniquePopupHash => 0;
        public bool ShowInScreenCenter => true;

        private string shipName;
        private string fileName;
        private int makeSandbox;
        private int makePermadeath;
        private int regenUniverse;
        private int regenStasis;
        private int resetResearch;
        private int resetWallet;
        private int resetMats;
        private int resetItems;
        private int resetQuests;
        private int resetTime;
        private int startWrecked;
        private int removeDatoids;
        private int resetStocks;
        private string storyGen;
        private string difficulty;
        private string universeSeed;
        private readonly List<string> keepRegenOptions = new() {
            T.VerbKeep,
            T.VerbRegenerate,
        };
        private List<string> researchOptions => _researchOptions ??= CreateResearchOptions();
        private List<string> _researchOptions;
        private List<string> walletOptions => _walletOptions ??= CreateWalletOptions();
        private List<string> _walletOptions;
        private List<string> invOptions => _invOptions ??= CreateInventoryOptions();
        private List<string> _invOptions;
        private readonly Dictionary<Species, int> resetSpecies = new();
        private readonly Dictionary<Species, int> resetSpeciesDead = new();

        private readonly List<string> keepResetOptions = new() {
            T.VerbKeep,
            T.Reset,
        };
        private readonly List<string> keepRemoveOptions = new() {
            T.VerbKeep,
            T.Remove,
        };
        private readonly List<string> noYesOptions = new() {
            T.No,
            T.Yes,
        };
        private readonly List<string> permadeathOptions = new() {
            T.SaveLoadAnytime,
            T.ModifierNoreload,
        };

        // Widget ref for modifiers button live-update
        private WButtonText modifiersWidget;

        private GameState S;

        public CloneGameUI(GameState s) {
            S = s;
            var gen = s.Query.GetStoryGen.Ask();
            storyGen = gen.NameT;
            difficulty = s.Difficulty.Name;
            regenUniverse = 1;
            removeDatoids = 1;
            // unlock tear 1
            resetResearch = 2;
            resetTime = 1;
            resetQuests = 1;
            resetStocks = 1;
            resetWallet = 3;
            regenStasis = 1;
            Modifier.ResetAll(null);
        }

        public static void ShowTemplatePopup() {
            WPopup.Create(false, IconId.WSaveGame, T.Warning, null, T.GameTemplateMode)
                .WithAction(T.Ok, null)
                .WithAction2(T.GameNew, new CloneGameUI(The.R.State).Show)
                .Spawn(center: true);
        }

        public void Show() {
            if (UIShowing.Overlay) {
                S.Sig.HideOverlay.Send();
            }
            S.Clock.Pause();
            S.UIQuery.ShowContextPopup.Ask(this, true);
        }

        public void Hide() => S.UIQuery.ShowContextPopup.Ask(this, false);

        private void Refresh() {
            if (S != null) Show();
        }

        public void OnPopupOpened() {
            S.Sig.ModifierChanged.AddListener(OnModifierChanged);
        }

        public void OnPopupClosed() {
            S?.Sig?.ModifierChanged.RemoveListener(OnModifierChanged);
            modifiersWidget = null;
            S = null;
        }

        private void OnModifierChanged(Modifier _) {
            UpdateModifiersWidget();
        }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("CloneGame");
            int blockIdx = 0;
            int footerIdx = 0;

            // --- Footer: Embark button ---
            var embarkBtn = UISpawn.Widget<WButtonText>(life,
                UIPrefabId.WPanelButton_Confirm, wFooter.Container, footerIdx++);
            embarkBtn.Load(T.VerbEmbark,
                () => Coroutines.StartDirect(CloneGame(S)));
            wFooter.SetActiveFast(true);

            // --- Ship Name ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.MenuGameName);
                if (shipName == null) {
                    shipName = GenUtils.NewGameShipName(A.S);
                    fileName = Texts.CleanupFileName(shipName);
                }
                var nameInput = UISpawn.Widget<WInputWithIcon>(life,
                    UIPrefabId.WContent2Input, block.Container, idx++);
                nameInput.Icon.Load(IconId.CSpaceShip);
                nameInput.Input.Load(T.MenuGameNameEnter, shipName, v => {
                    if (!string.IsNullOrWhiteSpace(v)) {
                        shipName = v;
                        fileName = Texts.CleanupFileName(v);
                    }
                });
                var randNameBtn = UISpawn.Widget<WButtonText>(life,
                    UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
                randNameBtn.Load(T.Randomize, () => {
                    var name = GenUtils.NewGameShipName(A.S);
                    nameInput.Input.SetValue(name);
                    shipName = name;
                    fileName = Texts.CleanupFileName(name);
                });
            }

            // --- Start Wrecked ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.Destroy);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CExplosion);
                row.Dropdown.Load(noYesOptions, startWrecked, v => {
                    startWrecked = v;
                    Refresh();
                });
            }

            // --- Universe ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.SpaceObjectUniverse);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CStarmap);
                row.Dropdown.Load(keepRegenOptions, regenUniverse, v => {
                    regenUniverse = v;
                    Refresh();
                });
                if (regenUniverse == 1) {
                    if (universeSeed == null) {
                        universeSeed = S.Query.GenerateName.Ask(
                            NameRequest.Generic(NameType.Seed)).Name;
                    }
                    var seedInput = UISpawn.Widget<WInputWithIcon>(life,
                        UIPrefabId.WContent2Input, block.Container, idx++);
                    seedInput.Icon.Load(IconId.CLeaf);
                    seedInput.Input.Load(T.GameSeedPlaceholder, universeSeed, v => {
                        universeSeed = v;
                    });
                    var randSeedBtn = UISpawn.Widget<WButtonText>(life,
                        UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
                    randSeedBtn.Load(T.Randomize, () => {
                        var name = S.Query.GenerateName.Ask(NameRequest.Generic(NameType.Seed));
                        seedInput.Input.SetValue(name.Name);
                        universeSeed = name.Name;
                    });
                }
            }

            // --- Story Gen ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.GameStorygen);
                var genNames = StoryGenDef.Ordered.Select(g => g.NameT).ToList();
                var selectedIdx = Mathf.Max(0, genNames.IndexOf(storyGen));
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CDNA);
                UIUtils.AddTip(life, row, T.GameStorygenTip);
                row.Dropdown.Load(genNames, selectedIdx, v => {
                    storyGen = genNames[v];
                });
            }

            // --- Difficulty ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.GameDifficulty);
                var difficulties = ModifierDifficulty.Difficulties;
                var selectedIdx = Mathf.Max(0, difficulties.IndexOf(difficulty));
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CExplosion);
                UIUtils.AddTip(life, row, T.GameDifficultyTip);
                row.Dropdown.Load(difficulties, selectedIdx, v => {
                    difficulty = difficulties[v];
                });
            }

            // --- Permadeath ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.PermadeathLabel);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CSkull);
                row.Dropdown.Load(permadeathOptions, makePermadeath, v => {
                    makePermadeath = v;
                });
            }

            // --- Stasis ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.ObjectsDevicesStasisarray);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(GraphicsId.ObjectsDevicesStasisArrayMini);
                row.Dropdown.Load(keepRegenOptions, regenStasis, v => {
                    regenStasis = v;
                });
            }

            // --- Sandbox ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.ScenarioSandbox);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.WDev);
                row.Dropdown.Load(noYesOptions, makeSandbox, v => {
                    makeSandbox = v;
                });
            }

            // --- Crew (per species, alive + dead) ---
            if (startWrecked == 0) {
                var allBeings = S.Query.AllBeingData.Ask();
                var countsAlive = new Dictionary<Species, int>();
                var countsDead = new Dictionary<Species, int>();
                var countsAll = new Dictionary<Species, int>();
                foreach (var being in allBeings.Values) {
                    if (!being.IsPlayer && being.Body.MindSlot?.IsPartRemoved != false) {
                        continue;
                    }
                    Dictionary<Species, int> count = null;
                    if (being.HasStateFlag(BeingState.Alive)) {
                        count = countsAlive;
                    } else if (being.HasStateFlag(BeingState.Dead)) {
                        count = countsDead;
                    } else {
                        continue;
                    }
                    if (!count.ContainsKey(being.Species)) count[being.Species] = 0;
                    count[being.Species]++;
                    if (!countsAll.ContainsKey(being.Species)) countsAll[being.Species] = 0;
                    countsAll[being.Species]++;
                }

                foreach (var sp in countsAll) {
                    if (!countsAlive.TryGetValue(sp.Key, out var cntAlive)) cntAlive = 0;
                    if (!countsDead.TryGetValue(sp.Key, out var cntDead)) cntDead = 0;
                    if (!resetSpecies.ContainsKey(sp.Key)) resetSpecies[sp.Key] = 0;
                    if (!resetSpeciesDead.ContainsKey(sp.Key)) resetSpeciesDead[sp.Key] = cntDead;

                    var block = UISpawn.Block(life, wContent, blockIdx++);
                    int idx = 0;
                    var spHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                    spHeader.Load(sp.Key.Name);

                    if (cntAlive > 0) {
                        var optsAlive = new List<string>();
                        for (int i = 0; i <= cntAlive; i++) optsAlive.Add($"{T.VerbKeep} {cntAlive - i}");
                        var aliveRow = UISpawn.Widget<WDropdownWithIcon>(life,
                            UIPrefabId.WContent2Dropdown, block.Container, idx++);
                        aliveRow.Icon.Load(sp.Key.Def.Preview);
                        UIUtils.AddTip(life, aliveRow, $"{T.BeingStateAlive}: {Units.XNum(cntAlive)}");
                        var capturedSp = sp.Key;
                        aliveRow.Dropdown.Load(optsAlive, resetSpecies[sp.Key], v => {
                            resetSpecies[capturedSp] = v;
                        });
                    }

                    if (cntDead > 0) {
                        var optsDead = new List<string>();
                        for (int i = 0; i <= cntDead; i++) optsDead.Add($"{T.VerbKeep} {cntDead - i}");
                        var deadRow = UISpawn.Widget<WDropdownWithIcon>(life,
                            UIPrefabId.WContent2Dropdown, block.Container, idx++);
                        deadRow.Icon.Load(IconId.CSkull);
                        UIUtils.AddTip(life, deadRow, $"{T.Dead}: {Units.XNum(cntDead)}");
                        var capturedSp = sp.Key;
                        deadRow.Dropdown.Load(optsDead, resetSpeciesDead[sp.Key], v => {
                            resetSpeciesDead[capturedSp] = v;
                        });
                    }
                }
            }

            // --- Time ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.NounTime);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CTime);
                row.Dropdown.Load(keepResetOptions, resetTime, v => {
                    resetTime = v;
                    Refresh();
                });
            }

            // --- Research ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.ResearchTree);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CScience);
                row.Dropdown.Load(researchOptions, resetResearch, v => {
                    resetResearch = v;
                });
                var datoidRow = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                datoidRow.Icon.Load(GraphicsId.ObjDatoidsDatoidBasic);
                datoidRow.Dropdown.Load(keepRemoveOptions, removeDatoids, v => {
                    removeDatoids = v;
                });
            }

            // --- Quests ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.Quests);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CQuest);
                row.Dropdown.Load(keepResetOptions, resetQuests, v => {
                    resetQuests = v;
                });
            }

            // --- Wallet ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.Wallet);
                var row = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                row.Icon.Load(IconId.CStarcreds);
                row.Dropdown.Load(walletOptions, resetWallet, v => {
                    resetWallet = v;
                });
                if (resetTime == 0) {
                    var stocksRow = UISpawn.Widget<WDropdownWithIcon>(life,
                        UIPrefabId.WContent2Dropdown, block.Container, idx++);
                    stocksRow.Icon.Load(IconId.CStockMarket);
                    stocksRow.Dropdown.Load(keepResetOptions, resetStocks, v => {
                        resetStocks = v;
                    });
                }
            }

            // --- Inventory ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var matsHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                matsHeader.Load($"{T.MenuMaterials} + {T.Food}");
                var matsRow = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                matsRow.Icon.Load(IconId.WMaterials);
                matsRow.Dropdown.Load(invOptions, resetMats, v => {
                    resetMats = v;
                });
                var itemsHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                itemsHeader.Load($"{T.Equipment} + {T.NounBodyParts}");
                var itemsRow = UISpawn.Widget<WDropdownWithIcon>(life,
                    UIPrefabId.WContent2Dropdown, block.Container, idx++);
                itemsRow.Icon.Load(IconId.CShield);
                itemsRow.Dropdown.Load(invOptions, resetItems, v => {
                    resetItems = v;
                });
            }

            // --- Modifiers ---
            {
                var block = UISpawn.Block(life, wContent, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
                header.Load(T.Modifiers);
                modifiersWidget = UISpawn.Widget<WButtonText>(life,
                    UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
                modifiersWidget.Load(T.Modifiers, () => {
                    S.UIQuery.ShowContextPopup.Ask(new ModifiersWidget(), true);
                });
                UIUtils.AddTipFunc(life, modifiersWidget, () => {
                    var enabledMods = new List<string>();
                    foreach (var m in Modifier.All.OrderBy(m => m.SortOrder).ThenBy(m => m.NameT)) {
                        if ((m.CanChangeInGame || m.ForceShowInCloneGame) && m.IsModified) {
                            enabledMods.Add(m.NameT);
                        }
                    }
                    return enabledMods.Count > 0 ? string.Join("\n", enabledMods) : T.ModifiersTip;
                });
                UpdateModifiersWidget();
            }

            return T.PrepareToEmbark;
        }

        private void UpdateModifiersWidget() {
            if (modifiersWidget == null) return;
            var enabledMods = new List<string>();
            int enabledCount = 0;
            int totalCount = 0;
            foreach (var m in Modifier.All.OrderBy(m => m.SortOrder).ThenBy(m => m.NameT)) {
                if (m.CanChangeInGame || m.ForceShowInCloneGame) {
                    totalCount++;
                } else {
                    continue;
                }
                if (m.IsModified) {
                    enabledCount++;
                    enabledMods.Add(m.NameT);
                }
            }
            modifiersWidget.SetText(string.Format("{0} [{1}/{2}]", T.Modifiers, enabledCount, totalCount));
        }

        private List<string> CreateResearchOptions() {
            var maxTier = S.Research.ResearchTierCounts.Keys.Max();
            var opts = new List<string>(1 + maxTier) {
                T.VerbKeep,
                $"{T.ResearchLocked}: {T.All}",
            };
            for (int i = 1; i <= maxTier; i++) {
                opts.Add($"{T.ResearchUnlock}: {T.TierNum_V1.F(i)}");
            }
            return opts;
        }
        private List<string> CreateWalletOptions() {
            var opts = new List<string> {
                $"{T.VerbKeep} ({S.Wallet.ValueString})", //0
                Wallet.ToValueString(0), //1
                Wallet.ToValueString(1000), //2
                Wallet.ToValueString(10000), //3
                Wallet.ToValueString(25000), //4
                Wallet.ToValueString(50000), //5
                Wallet.ToValueString(100000), //6
                Wallet.ToValueString(1000000) //7
            };
            return opts;
        }
        private List<string> CreateInventoryOptions() {
            var opts = new List<string> {
                $"{T.VerbKeep}: {Units.Percentage(1f)}", // 0
                $"{T.VerbKeep}: {Units.Percentage(0.9f)}", // 1
                $"{T.VerbKeep}: {Units.Percentage(0.8f)}", // 2
                $"{T.VerbKeep}: {Units.Percentage(0.7f)}", // 3
                $"{T.VerbKeep}: {Units.Percentage(0.6f)}", // 4
                $"{T.VerbKeep}: {Units.Percentage(0.5f)}", // 5
                $"{T.VerbKeep}: {Units.Percentage(0.4f)}", // 6
                $"{T.VerbKeep}: {Units.Percentage(0.3f)}", // 7
                $"{T.VerbKeep}: {Units.Percentage(0.2f)}", // 8
                $"{T.VerbKeep}: {Units.Percentage(0.1f)}", // 9
                $"{T.VerbKeep}: {Units.Percentage(0f)}", // 10
            };
            return opts;
        }

        private void ResetInventory() {
            if (resetMats > 0) {
                var removePct = 0.1f * resetMats;
                var toRemove = Caches.MatCounts;
                S.Sys.Inventory.LoadRemainingMaterials(toRemove);
                if (removePct < 1f) {
                    foreach (var mat in toRemove.ToList()) {
                        toRemove[mat.Key] = Mathf.CeilToInt(mat.Value * removePct);
                    }
                }

                foreach (IMatStorage storage in S.Sys.Inventory.Storages.ToList()) {
                    if (storage.IsEmpty) { continue; }
                    if (storage.Contents == null) {
                        D.Err("Non-empty storage but contents is null! {0}", storage);
                        continue;
                    }
                    foreach (var matCnt in storage.Contents.ToArray()) {
                        var mat = matCnt.Key;
                        if (mat.Group == "Datoid") { continue; }
                        var leftToRemove = toRemove.Get(mat, 0);
                        if (leftToRemove > 0) {
                            var toTake = Mathf.Min(leftToRemove, matCnt.Value);
                            var took = storage.Take(mat, null, toTake, "TrimMats");
                            toRemove[mat] = leftToRemove - took;
                        }
                    }
                }
            }
            if (resetItems > 0) {
                var removePct = 0.1f * resetItems;
                var toRemove = new Dictionary<Def, int>();
                var allObjs = S.Query.GetAllObjs.Ask();
                foreach (var obj in allObjs.Values) {
                    if (obj.State == ObjState.Destroyed || obj.State == ObjState.Equipped) {
                        continue;
                    }
                    var def = obj.Def;
                    // don't destroy eggs, plants, raw mats, food, etc
                    if (obj.Item == null && obj.BodyPart == null) {
                        continue;
                    }
                    if (!toRemove.ContainsKey(def)) {
                        toRemove[def] = 0;
                    }
                    toRemove[def]++;
                }
                D.Warn("Before To remove: {0}", D.DumpCollection(toRemove));
                foreach (var def in toRemove.Keys.ToList()) {
                    toRemove[def] = Mathf.CeilToInt(toRemove[def] * removePct);
                }
                D.Warn("After To remove: {0}", D.DumpCollection(toRemove));

                foreach (var obj in allObjs.Values.ToList()) {
                    var def = obj.Def;
                    if (toRemove.TryGetValue(def, out var count) && count > 0) {
                        D.Warn("Will remove: {0}", obj);
                        if (obj.State == ObjState.Stored) {
                            var storage = S.Components.Find<ObjStorageComp>(obj.StorageId);
                            D.Warn("Will unstore: {0} from {1}", obj, storage);
                            if (storage != null) {
                                if (!S.Query.UnstoreObj.Ask(obj, storage)) {
                                    D.Err("Failed unstore: {0} from {1}", obj, storage);
                                }
                            }
                        }
                        if (obj.Obj != null) {
                            D.Warn("Destroying: {0}", obj);
                            S.Objs.Destroy(obj.Obj);
                        } else {
                            D.Err("obj.Obj is null! {0}", obj);
                        }
                        toRemove[def]--;
                    }
                }
            }
        }

        private void ResetWallet() {
            switch (resetWallet) {
                case 0: break;
                case 1: S.Wallet.Amount = 0; break;
                case 2: S.Wallet.Amount = 1000; break;
                case 3: S.Wallet.Amount = 10000; break;
                case 4: S.Wallet.Amount = 25000; break;
                case 5: S.Wallet.Amount = 50000; break;
                case 6: S.Wallet.Amount = 100000; break;
                case 7: S.Wallet.Amount = 1000000; break;
            }

            if (resetStocks == 1 || resetTime == 1) {
                S.Portfolio = null;
                S.Market.Reset();
            }
        }

        private void ResetResearch() {
            if (removeDatoids == 1) {
                RemoveDatoids();
            }
            if (resetResearch == 0) { return; }
            S.Research.ResetUpToLevel(resetResearch - 1);
        }

        private void RemoveDatoids() {
            // Piles first, all of them: a carried pile can be out of the inventory, and a pile's
            // contents leave out what is reserved. Whoever holds one lets go first, and the
            // piles are looked up again after that, because a dropped pile can merge into another
            foreach (var pile in S.Components.GenFind<UnstoredMatComp>()) {
                if (IsDatoid(pile.Type)) {
                    pile.ReleaseHolders();
                }
            }
            foreach (var pile in S.Components.GenFind<UnstoredMatComp>()) {
                if (IsDatoid(pile.Type) && pile.Entity.IsActive && pile.StackSize > 0) {
                    pile.Take(pile.Type, null, pile.StackSize, "RemoveDatoids");
                }
            }
            foreach (IMatStorage storage in S.Sys.Inventory.Storages.ToList()) {
                if (storage is UnstoredMatComp || storage.IsEmpty) { continue; }
                if (storage.Contents == null) {
                    D.Err("Non-empty storage but contents is null! {0}", storage);
                    continue;
                }
                foreach (var matCnt in storage.Contents.ToArray()) {
                    if (IsDatoid(matCnt.Key) && matCnt.Value > 0) {
                        storage.Take(matCnt.Key, null, matCnt.Value, "RemoveDatoids");
                    }
                }
            }
        }

        private static bool IsDatoid(MatType mat) {
            return mat?.Group == "Datoid";
        }

        private void ResetCrew() {
            var allBeings = S.Query.AllBeingData.Ask();

            // Remove beings
            var allBeingsReversed = allBeings.Values.ToList();
            allBeingsReversed.Reverse();
            foreach (var being in allBeingsReversed) {
                if (startWrecked == 1) {
                    RemoveBeing(being);
                    continue;
                }
                // Remove non-players first
                if (!being.IsPlayer && being.Body.MindSlot?.IsPartRemoved != false) {
                    RemoveBeing(being);
                    continue;
                }

                // Remove according to calculated counts
                if (being.HasStateFlag(BeingState.Alive) &&
                        resetSpecies.TryGetValue(being.Species, out var countAlive) &&
                            countAlive > 0) {
                    resetSpecies[being.Species]--;
                    RemoveBeing(being);
                } else if (being.HasStateFlag(BeingState.Dead) &&
                        resetSpeciesDead.TryGetValue(being.Species, out var countDead) &&
                            countDead > 0) {
                    resetSpeciesDead[being.Species]--;
                    RemoveBeing(being);
                }
            }
        }

        private void RemoveBeing(BeingData beingData) {
            // D.Warn("Removing being: {0}", beingData);
            if (beingData.StateFlags.HasFlag(BeingState.Stored)) {
                var storage = S.Components.Find<BeingStorageComp>(beingData.StorageId);
                if (storage != null) {
                    S.Query.UnstoreBeing.Ask(beingData, storage);
                }
            }
            BeingUtils.Eliminate(S, beingData, null, 0);
        }

        #if !PRODUCTION_BUILD
        // The Workshop validation run (ModValidationDriver): Embark with the popup's defaults,
        // the way a player who changes nothing would
        public IEnumerator EmbarkWithDefaults() {
            shipName = GenUtils.NewGameShipName(A.S);
            fileName = Texts.CleanupFileName(shipName);
            yield return CloneGame(S);
        }
        #endif

        private IEnumerator CloneGame(GameState s) {
            The.ScreenBlock.SetCenterText(string.Empty, 0f);
            The.ScreenBlock.SetSideText(string.Empty);
            The.ScreenBlock.Show();
            yield return null;
            yield return WreckShip();
            DoCloneGame(s);

            yield return null;
            yield return EnsureCrew();
            The.ScreenBlock.HideImmediate();
            Hide();
            yield return null;
            var save = new CmdSaveGame(s, fileName, false, false);
            save.ExecuteSync(false);
            var meta = save.SavedMeta;
            yield return save.TakeScreenshot();
            yield return null;
            // The.ScreenBlock.ShowLoading();
            The.CleanupAfterGame();
            meta.SaveName = Files.CleanPath(meta.GameName);
            meta.SaveDir = SaveLoadUtils.SaveDir(meta.SaveName);
            The.SysSig.ShowLoadingScreen.Send($"Loading: {meta.GameName}", true);
            yield return new CmdLoadGame(meta, showMainMenu: false).ExecuteAsync();

            // The.BB.Set(BBKeyH.GlobalPostInitAction, "load_game");
            // The.BB.Set(BBKeyH.GlobalSaveName, $"{s.SaveDir}/{fileName}");
            // The.BB.Set(BBKeyH.GlobalGameName, shipName);
            // MiscUtils.LoadSceneTimed("Empty");
            // MiscUtils.LoadSceneTimed("Game");
            SaveLoadUtils.ClearCache();
            yield return null;
            Coroutines.StartDirect(CameraControls.FadeCamera());
            // The.ScreenBlock.Hide();
            The.SysSig.GameCloned.Send();
        }

        private IEnumerator EnsureCrew() {
            if (startWrecked == 1) {
                foreach (var b in S.Beings.Alive) {
                    D.Err("Removing extra alive being: {0}", b);
                    BeingUtils.Eliminate(S, b.Data, null, 0);
                }
                yield return null;
            }
            if (S.Beings.AliveCount == 0 || startWrecked == 1) {
                var scenario = Scenario.Find(WreckedEmptyShipScenario.ScenarioId);
                var counts = scenario.GetStartingBeingCounts();
                var areas = S.Sys.Areas.SolidArea;
                int countAdded = 0;
                foreach (var cnt in counts) {
                    var count = ModifierBeingsCount.ValueFor(S, cnt.Item1.Id,
                        out var defaultVal, scenario);
                    for (int i = 0; i < count; i++) {
                        var pos = FindBestPosForBeing(cnt.Item1, areas, S.Rng);
                        var beingData = BeingUtils.GenBeingWithInventory(cnt.Item1.IdH,
                            BeingType.StartingCrew, FactionDef.Player, GenItemFlags.Casual | GenItemFlags.SpaceReady);
                        S.Beings.Create(pos, beingData);
                        countAdded++;
                    }
                }
                if (countAdded == 0) {
                    var pos = FindBestPosForBeing(Species.Get(SpeciesIdH.Drone), areas, S.Rng);
                    var beingData = BeingUtils.GenBeingWithInventory(SpeciesIdH.Drone,
                        BeingType.StartingCrew, FactionDef.Player, GenItemFlags.Casual | GenItemFlags.SpaceReady);
                    S.Beings.Create(pos, beingData);
                }
                yield return null;
            }
        }

        private int FindBestPosForBeing(Species species, List<int> positions, Rng rng) {
            var needsSafety = species.IsBiological;

            var safeTempMin = Units.CelsiusToKelvin(10f);
            var safeTempMax = Units.CelsiusToKelvin(40f);
            var safeO2 = 0.17f;
            var good = positions.Where(p => IsGoodStart(p));
            if (needsSafety) {
                var safe = good.Where(p => IsSafePos(p, safeTempMin, safeTempMax, safeO2));
                if (safe.Count() > 0) {
                    good = safe;
                }
            }
            var best = good.OrderBy(p => rng.Value).FirstOrDefault();
            if (best < 1) {
                best = positions.OrderBy(p => rng.Value).FirstOrDefault();
            }
            return best;
        }

        private bool IsGoodStart(int pos) {
            int walkableNeighborCount = 0;
            Pos.ForEachAround(pos, 1, false, false, (p) => {
                if (!EntityUtils.IsEmptyFloorAt(p)) {
                    walkableNeighborCount++;
                }
                return true;
            });
            return walkableNeighborCount > 2;
        }

        private bool IsSafePos(int pos, float minTemp, float maxTemp, float minO2) {
            if (S.GetO2(pos) < minO2) { return false; }
            if (S.GetTemp(pos) < minTemp) { return false; }
            if (S.GetTemp(pos) > maxTemp) { return false; }
            return true;
        }

        private IEnumerator WreckShip() {
            if (startWrecked != 1) {
                yield break;
            }
            Ready.SetGen(false, "Wrecking ship");

            var b = S.Sys.Areas.ShipBounds;
            var rng = new Rng(Strings.ToSeed(shipName));
            var factory = new ShipFactory(b, rng.Int, false);
            factory.Initialize(S, false);
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(c => c.Tile.IsConstructed);
            if (cpu != null) {
                factory.Spec.SetCPUPos(cpu.Tile.PosIdx);
                Pos.ForEachAround(cpu.Tile.CenterIdx, 3, true, true, (p) => {
                    factory.Spec.InvincibleParts.Add(p);
                    // D.Warn("Adding invincible pos: {0}", Pos.String(p));
                    return true;
                });
            }
            var stasis = S.Components.AllByType<StasisArrayComp>();
            if (!Arrays.IsEmpty(stasis)) {
                foreach (var comp in stasis) {
                    factory.Spec.AddStasisPos(comp.Entity.PosIdx);
                    Pos.ForEachAround(comp.Entity.CenterIdx, 4, true, true, (p) => {
                        factory.Spec.InvincibleParts.Add(p);
                        // D.Warn("Adding invincible pos: {0}", Pos.String(p));
                        return true;
                    });
                }
            }

            GenShip.AddDestructionStages(factory, rng, 1200);
            yield return factory.RunStages(S);
            D.Warn("Wrecked ship");
            Ready.SetGen(true, "Done Wrecking ship");
            yield return null;
            S.Sig.ForceAreas.Send();
            S.Clock.TryForceTick();
            yield return null;
        }

        private void DoCloneGame(GameState s) {
            s.Shared = null;
            s.GameName = shipName;
            s.SaveDir = SaveLoadUtils.SaveDir(fileName);
            s.Vars.IsTemplateMode = false;

            // clear log
            // GameObject.FindFirstObjectByType<EventLogView>().Clear();

            // Scenario
            Scenario scenario;
            if (makeSandbox == 1f) {
                scenario = Scenario.Find(SandboxScenario.ScenarioId);
                S.Prefs.SetBool(Pref.DevInstantBuild, true);
            } else {
                scenario = Scenario.Find(ExistingShipScenario.ScenarioId);
                S.Prefs.SetBool(Pref.DevInstantBuild, false);
            }
            scenario.Data = new ComponentData(0, scenario.Id);
            s.Scenario = scenario;

            // Story generator
            if (resetTime == 1) {
                // The clock is not wound back: every stored tick (mood effects, task retry
                // backoff, birth ticks...) is on the old timeline, and a rewind left them
                // thousands of days in the future
                S.Playtime = 0f;
                S.SessionTime = 0f;
                s.Map.SituationHistory?.Clear();
                s.Sys.Electricity.History?.Clear();
                s.Market.History?.Clear();

            }
            var sg = StoryGenDef.All.Values.FirstOrDefault(
                g => !g.Internal && g.NameT == storyGen);
            sg ??= StoryGenDef.Default;
            s.Sig.SetStoryGen.Send(sg);

            // Difficulty
            s.Difficulty = Difficulty.FindByName(difficulty);

            // Permadeath
            s.IsPermadeath = makePermadeath == 1;
            // The clone is saved and loaded right away, and loading applies the modifier
            ModifierNoReload.Instance.IsActive = s.IsPermadeath;

            if (regenStasis == 1) {
                // 1. do we have any stasis arrays?
                var arrays = S.Components.AllByType<StasisArrayComp>();
                if (!Arrays.IsEmpty(arrays)) {
                    var manifest = ManifestSys.For(S);
                    foreach (StasisArrayComp array in arrays) {
                        manifest.EnsurePopulated(array);
                        if (array.BeingCount < ManifestSys.DefaultHumans) {
                            var pets = array.BeingCount == 0
                                ? Rng.URange(ManifestSys.DefaultMinPets, ManifestSys.DefaultMaxPets + 1)
                                : 0;
                            manifest.Populate(array,
                                ManifestSys.DefaultHumans - array.BeingCount, pets, Rng.Unseeded);
                        }
                    }
                } else {
                    Ready.SetGen(false, "Generating stasis");
                    var stage = new StasisAreaGenStage(ManifestSys.DefaultHumans, ManifestSys.DefaultHumans,
                        ManifestSys.DefaultMinPets, ManifestSys.DefaultMaxPets, 0, 0, 0);
                    // The spec seed picks the stasis floor; the new game path sets it, this one did not
                    stage.Initialize(new ShipSpec { Seed = Rng.UInt }, S);
                    stage.SetRng(Rng.Unseeded);
                    stage.Apply();
                    Ready.SetGen(true, "Done generating stasis");
                    if (stage.StartOver) {
                        D.Err("Failed to find place for stasis array! Won't generate");
                    }
                }
            }

            if (resetQuests == 1) {
                S.Query.ResetAllQuests.Ask();
            }

            if (regenUniverse == 1) {
                if (!string.IsNullOrWhiteSpace(universeSeed)) {
                    s.SeedSpaceStr = universeSeed;
                    s.SeedSpace = Strings.ToSeed(universeSeed);
                }
                s.Sys.Universe.Regenerate(s.SeedSpace);
            }

            // research
            ResetResearch();
            ResetWallet();
            ResetInventory();
            ResetCrew();
        }
    }
}
