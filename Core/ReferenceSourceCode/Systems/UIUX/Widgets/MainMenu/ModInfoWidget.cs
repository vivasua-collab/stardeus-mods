using System;
using System.IO;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Mods;
using Game.Platform;
using Game.Platform.Steam;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ModInfoWidget : BaseWidget {
        [SerializeField] private WButton closeButton;
        [SerializeField] private WText modNameText;
        [SerializeField] private WText modVersionText;
        [SerializeField] private WText modDescText;
        [SerializeField] private WButtonIcon btnMoreActions;
        [SerializeField] private WButtonText btnMainAction;
        [SerializeField] private Image screenshot;
        [SerializeField] private AutoSizeImageLayoutElement screenshotAutoSize;
        private ModInfo mod;
        private Texture2D tex;
        private SharedSaveInfo shipInfo;
        private string shipPath;
        private GameStateMeta shipMeta;

        private void Awake() {
            closeButton.Load(Close);
        }

        #if !DISABLESTEAMWORKS
        private void UnsubscribeMod() {
            if (The.Platform is not SteamPlatform steam) {
                UISounds.PlayActionDenied();
                PlatformUtils.ShowSteamUnavailable();
                return;
            }
            steam.Workshop.UnsubscribeItem(mod.SteamWorkshopId, (success) => {
                if (!success) {
                    UISounds.PlayActionDenied();
                    return;
                }
                if (The.ModLoader.AvailableMods.Contains(mod.Id)) {
                    The.ModLoader.AvailableMods.Remove(mod.Id);
                    The.ModLoader.LoadedMods.Remove(mod);
                    if (!mod.FullPath.Replace("\\", "/").Contains("/Mods/")) {
                        D.Warn("Deleting mod dir: {0}", mod.FullPath);
                        try {
                            Files.ClearDirectory(mod.FullPath, true);
                        } catch (Exception ex) {
                            D.LogEx(ex, "Failed to cleanup mod directory: {0}", mod.FullPath);
                        }
                    } else {
                        D.Warn("Won't delete mod directory: {0}", mod.FullPath);
                    }
                    UISounds.PlayDigitalConfirm();
                    PlatformUtils.SuggestRestart();
                } else {
                    UISounds.PlayActionDenied();
                }
            });
        }

        private void UnsubscribeShip() {
            if (!ulong.TryParse(shipInfo.SteamWorkshopId, out var wsId)) {
                UISounds.PlayActionDenied();
                return;
            }
            if (The.Platform is not SteamPlatform steam) {
                UISounds.PlayActionDenied();
                PlatformUtils.ShowSteamUnavailable();
                return;
            }
            steam.Workshop.UnsubscribeItem(wsId, (success) => {
                if (!success) {
                    UISounds.PlayActionDenied();
                    return;
                }
                The.ModLoader.Discovery.SharedSaves.Remove(shipPath);
                Files.ClearDirectory(shipPath, true);
                UISounds.PlayDigitalConfirm();
                Close();
            });
        }

        private void OpenInWorkshop() {
            Files.OpenOSUrl(SteamWorkshopItem.URLFor(mod));
        }

        private void OpenShipInWorkshop() {
            Files.OpenOSUrl(SteamWorkshopItem.URLFor(shipInfo.SteamWorkshopId));
        }

        private void UploadToSteam() {
            if (!ModLoader.IsModEnabled(mod.Id) || !The.ModLoader.IsModLoaded(mod.Id)) {
                UISounds.PlayActionDenied();
                WPopup.SystemWarning(T.Warning, "Enable and load the mod first");
                return;
            }
            if (The.Platform is not SteamPlatform steam) {
                UISounds.PlayActionDenied();
                PlatformUtils.ShowSteamUnavailable();
                return;
            }
            if (!The.Prefs.GetBool(Pref.GModUploadTermsAccepted, PrefH.GModUploadTermsAccepted, false)) {
                WPopup.Create(true, IconId.CCheck, "Stardeus Modding Terms Agreement",
                    Strings.ParseSteamNews("Please read Stardeus Modding Terms in EULA before uploading your mod. Click this URL to open the EULA in browser: https://stardeusgame.com/eula/",
                        Texts.TMPColorAccented))
                    .WithAction("Agree", () => {
                        The.Prefs.SetBool(Pref.GModUploadTermsAccepted, true);
                        steam.Workshop.CreateOrUpdate(mod);
                    })
                    .WithAction2(T.Cancel, null)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            } else {
                steam.Workshop.CreateOrUpdate(mod);
            }
        }
        #endif

        private void OpenModDir() {
            Files.OpenOSFolder(mod.ModFolder(""));
            UISounds.PlayPopup();
        }

        private void OpenShipDir() {
            Files.OpenOSFolder(shipPath);
            UISounds.PlayPopup();
        }

        public override void Toggle(bool show) {
            if (show) {
                if (gameObject.activeSelf) {
                    return;
                }
                gameObject.SetActive(true);
            } else {
                Close();
            }
        }

        public void Close() {
            gameObject.SetActiveFast(false);
            mod = null;
            shipMeta = null;
            shipPath = null;
        }

        public void Load(ModInfo mod) {
            this.mod = mod;

            if (mod.Cheat) {
                modDescText.Load($"{Texts.Red(CheatMods.Note)}{GlyphMap.BRHR}{mod.Description}");
            } else {
                modDescText.Load(mod.Description);
            }
            modNameText.Load(mod.NameDecorated);
            modVersionText.Load(mod.ModVersion);

            bool isModInUploadableDir = The.ModLoader.Discovery.IsUploadable(mod.FullPath);
            bool showSteamUpload = mod.SteamWorkshop && isModInUploadableDir
                && !The.Platform.IsConsole();
            #if UNITY_EDITOR
            if (isModInUploadableDir) {
                showSteamUpload = true;
            }
            #endif
            bool showUnsub = mod.SteamWorkshopId > 0
                && The.ModLoader.Discovery.IsWorkshop(mod.FullPath);

            // Main action: open workshop page for WS mods, open directory otherwise
            #if !DISABLESTEAMWORKS
            if (mod.SteamWorkshopId > 0) {
                btnMainAction.Load(Texts.ExternalLink(T.OpenInSteamWorkshop), OpenInWorkshop);
            } else {
                btnMainAction.Load(Texts.ExternalLink(T.OpenDir), OpenModDir);
            }
            #else
            btnMainAction.Load(Texts.ExternalLink(T.OpenDir), OpenModDir);
            #endif
            btnMainAction.SetActiveFast(true);

            var isCoreMod = mod.Id == "Core" || mod.Id == "Translations";

            // Cog menu: secondary actions
            if (!isCoreMod) {
                var qc = QuickChoice.Create();
                qc.Widget = btnMoreActions.Rect;
                if (mod.SteamWorkshopId > 0) {
                    qc.AddButton(Texts.ExternalLink(T.OpenDir), OpenModDir);
                    if (showUnsub) {
                        #if !DISABLESTEAMWORKS
                        qc.AddButton(T.ModUnsubscribe, UnsubscribeMod);
                        #endif
                    }
                }
                #if !DISABLESTEAMWORKS
                if (showSteamUpload) {
                    qc.AddButton(T.UploadToSteam, UploadToSteam);
                } else if (isModInUploadableDir && mod.IsEnabled) {
                    qc.AddButton(Texts.Strike(T.UploadToSteam), UploadToSteamHowTo);
                }
                #endif
                if (qc.Count > 0) {
                    btnMoreActions.Load(IconId.CCog, qc.Show);
                    btnMoreActions.SetActiveFast(true);
                } else {
                    btnMoreActions.SetActiveFast(false);
                }
            } else {
                btnMoreActions.SetActiveFast(false);
            }

            LoadImage(mod.CoverPath);
        }

        private void UploadToSteamHowTo() {
            WPopup.SystemWarning(T.UploadToSteam,
                "If you are the author of this mod, set \"SteamWorkshop\" to true in your ModInfo.json to enable uploading.");
        }

        public void LoadShip(SharedSaveInfo info, string path) {
            shipInfo = info;
            shipPath = path;
            shipMeta = ReadShipMeta(path);

            modNameText.Load(info.Title);
            modVersionText.Load(info.GameVersion ?? string.Empty);
            modDescText.Load(info.Description ?? string.Empty);

            // Main action: start a new game from this ship
            btnMainAction.Load(T.GameNewDesc, StartNewGameFromShip);
            btnMainAction.SetActiveFast(shipMeta != null);

            // Cog menu: open workshop / dir / unsubscribe
            var isSteamWS = !string.IsNullOrEmpty(info.SteamWorkshopId)
                && info.SteamWorkshopId != "0";
            var qc = QuickChoice.Create();
            qc.Widget = btnMoreActions.Rect;
            if (isSteamWS) {
                #if !DISABLESTEAMWORKS
                qc.AddButton(Texts.ExternalLink(T.OpenInSteamWorkshop), OpenShipInWorkshop);
                #endif
                qc.AddButton(Texts.ExternalLink(T.OpenDir), OpenShipDir);
                #if !DISABLESTEAMWORKS
                qc.AddButton(T.ModUnsubscribe, UnsubscribeShip);
                #endif
            } else {
                qc.AddButton(Texts.ExternalLink(T.OpenDir), OpenShipDir);
            }
            btnMoreActions.Load(IconId.CCog, qc.Show);
            btnMoreActions.SetActiveFast(true);

            LoadImage($"{path}/_shared.jpg");
        }

        private void LoadImage(string imagePath) {
            if (!File.Exists(imagePath)) {
                screenshot.SetActiveFast(false);
            } else {
                if (tex != null) {
                    The.Alloc.UnregisterAndDispose(tex);
                }
                tex = Res.LoadTexture(imagePath, false, true);
                screenshot.sprite = Res.CreateSpriteFrom(tex);
                screenshot.SetActiveFast(true);
                screenshotAutoSize.Calculate();
            }
        }

        // The Workshop validation run starts ships through these three too (ModValidationDriver)
        public static GameStateMeta ReadShipMeta(string path) {
            var meta = SaveLoadUtils.DeserializeMeta($"{path}/_shared.meta", false);
            if (meta != null) {
                meta.SaveDir = path;
                meta.SaveName = "_shared";
            }
            return meta;
        }

        public static bool IsShipCompatible(GameStateMeta meta) {
            return meta.SaveVersion == Consts.CurrentSaveVersion;
        }

        public static void LoadShipTemplate(GameStateMeta meta) {
            meta.IsTemplateMode = true;
            SaveLoadUtils.LoadSave(meta);
        }

        private void StartNewGameFromShip() {
            if (shipMeta == null) { return; }
            if (!IsShipCompatible(shipMeta)) {
                WPopup.Create(false, IconId.CCross, T.LoadFailed,
                    T.LoadFailedIncompatible_V1.F(shipInfo.Title)).Spawn();
                return;
            }
            LoadShipTemplate(shipMeta);
            // var saveFile = Files.CombinePath(shipPath, "_shared");
            // The.BB.Set(BBKeyH.GlobalPostInitAction, "load_game");
            // The.BB.Set(BBKeyH.GlobalSaveName, saveFile);
            // The.BB.Set(BBKeyH.GlobalGameName, shipInfo.Title);
            // The.BB.Set(BBKeyH.GlobalSaveType, "template");
            // D.Warn("Loading ship as template: {0}", shipInfo.Title);
            // MiscUtils.LoadSceneTimed("Empty");
            // MiscUtils.LoadSceneTimed("Game");
            // SaveLoadUtils.ClearCache();
        }
    }
}
