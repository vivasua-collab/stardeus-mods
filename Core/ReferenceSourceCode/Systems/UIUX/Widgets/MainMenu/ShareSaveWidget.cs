#if !DISABLESTEAMWORKS
using System;
using System.Collections;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Platform;
using Game.Platform.Steam;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ShareSaveWidget : BaseWidget, IPoolableSingleton {
        [SerializeField] private WButton closeButton;
        [SerializeField] private WText inputHeader;
        [SerializeField] private WInput inputTitle;
        [SerializeField] private WText descriptionHeader;
        [SerializeField] private WInput inputDescription;
        [SerializeField] private Image screenshot;
        [SerializeField] private WButtonText btnUpload;

        private static ShareSaveWidget spawnedInstance;
        private static string lastTitle = "";
        private static string lastDescription = "";

        internal bool isInitialized;
        private Lifetime lifetime;
        private Texture2D tex;

        private void Awake() {
            closeButton.Load(Close);
        }

        public void Close() {
            lifetime?.Dispose();
            lifetime = null;
            spawnedInstance = null;
            PrefabPool.Despawn(gameObject);
        }

        private void OnDisable() {
            if (tex != null) {
                The.Alloc.UnregisterAndDispose(tex);
                tex = null;
            }
        }

        public static void Show() {
            if (spawnedInstance != null) {
                PrefabPool.Despawn(spawnedInstance.gameObject);
                spawnedInstance.lifetime?.Dispose();
                spawnedInstance.lifetime = null;
            }
            var prefab = ResourceLibrary.FindPrefab<ShareSaveWidget>();
            var widget = PrefabPool.Spawn(prefab, UIPoolRoot.SystemPopupContainer);
            widget.InternalLoad();
            spawnedInstance = widget;
            if (widget.isInitialized) {
                UIUtils.AfterShowPopup(widget.Rect, true, randShift: false);
            } else {
                Coroutines.OnFrame(3, () => {
                    widget.isInitialized = true;
                    UIUtils.AfterShowPopup(widget.Rect, true, randShift: false);
                });
            }
        }

        private void InternalLoad() {
            lifetime?.Dispose();
            lifetime = Lifetime.Create("ShareSave");
            if (string.IsNullOrWhiteSpace(lastTitle)) {
                lastTitle = The.R?.State?.GameName ?? "";
            }
            inputHeader.Load(T.MenuGameName);
            inputTitle.Load(T.MenuGameNameEnter, lastTitle, v => lastTitle = v);
            descriptionHeader.Load(T.About);
            inputDescription.Load(T.About, lastDescription, v => lastDescription = v);
            btnUpload.Load(SteamSharedSave.ButtonName, TryUpload);
            screenshot.SetActiveFast(false);
            Coroutines.Start(LoadScreenshotAndSave(), this, "ShareSaveWidget.Load", false, true);
        }

        private IEnumerator LoadScreenshotAndSave() {
            var texPath = SaveLoadUtils.SaveDir("_shared/_shared.jpg");
            if (tex != null) {
                The.Alloc.UnregisterAndDispose(tex);
                tex = null;
            }
            tex = Res.LoadTexture(texPath, false, true);
            if (tex != null) {
                screenshot.sprite = Res.CreateSpriteFrom(tex);
                yield return null;
                screenshot.SetActiveFast(true);
            }
            if (The.R?.State?.IsLoaded == true) {
                The.R.State.Clock.SetSpeed(ClockSpeed.Stopped);
                yield return null;
                while (!Ready.Save || The.R.State.IsSavingNow) {
                    yield return null;
                }
                var cmd = new CmdSaveGame(The.R.State, "_shared/_shared", false, false);
                cmd.ExecuteSync(false);
                yield return null;
            }
            if (The.R?.State?.Shared is SteamSharedSave ss && ss.SteamWorkshopId != 0) {
                btnUpload.Load(T.UpdateOnSteamWorkshop, TryUpload);
            }
        }

        private void TryUpload() {
            UploadToSteamWorkshop();
        }

        private void UploadToSteamWorkshop() {
            if (string.IsNullOrWhiteSpace(inputTitle.Text.Trim())) {
                WPopup.Create(false, IconId.CWarning, T.Error, T.FeedbackTitleRequired).Spawn();
                return;
            }

            if (The.Platform is not SteamPlatform steam) {
                UISounds.PlayActionDenied();
                PlatformUtils.ShowSteamUnavailable();
                return;
            }

            var info = new SharedSaveInfo {
                Title = inputTitle.Text.Trim(),
                Description = inputDescription.Text.Trim(),
                GameVersion = Consts.Version,
                Creator = The.Platform.PlayerName,
                ShareDate = DateTime.Now.ToString(Consts.DateFormatShort),
            };

            if (The.R.State.Shared is not SteamSharedSave sharedSave) {
                sharedSave = new SteamSharedSave();
                sharedSave.Info = info;
                The.R.State.Shared = sharedSave;
            }
            if (sharedSave.SteamWorkshopId != 0) {
                info.SteamWorkshopId = sharedSave.SteamWorkshopId.ToString();
                sharedSave.Info = info;
            }

            var json = JsonUtility.ToJson(info, true);
            var r = Persist.Write(SaveLoadUtils.SaveDir("_shared/SaveInfo.json"), json, false);
            if (!r.IsSuccess) {
                D.Err("SaveInfo.json write failed! {0}. {1}", r, json);
            }

            if (!The.Prefs.GetBool(Pref.GModUploadTermsAccepted, PrefH.GModUploadTermsAccepted, false)) {
                WPopup.Create(true, IconId.CCheck, "Stardeus Modding Terms Agreement",
                    Strings.ParseSteamNews(
                        "Please read Stardeus Modding Terms in EULA before uploading your mod. Click this URL to open the EULA in browser: https://stardeusgame.com/eula/",
                        Texts.TMPColorAccented))
                    .WithAction("Agree", () => {
                        The.Prefs.SetBool(Pref.GModUploadTermsAccepted, true);
                        steam.Workshop.CreateOrUpdate(sharedSave, () => {
                            info.SteamWorkshopId = sharedSave.SteamWorkshopId.ToString();
                            sharedSave.Info = info;
                            Persist.Write(SaveLoadUtils.SaveDir("_shared/SaveInfo.json"),
                                JsonUtility.ToJson(info, true), false);
                        });
                    })
                    .WithAction2(T.Cancel, null)
                    .Spawn();
            } else {
                steam.Workshop.CreateOrUpdate(sharedSave, () => {
                    info.SteamWorkshopId = sharedSave.SteamWorkshopId.ToString();
                    sharedSave.Info = info;
                    Persist.Write(SaveLoadUtils.SaveDir("_shared/SaveInfo.json"),
                        JsonUtility.ToJson(info, true), false);
                });
            }

            lastTitle = inputTitle.Text;
            lastDescription = inputDescription.Text;
            Ready.SetTextInput(true, "Sharing");
            Close();
        }
    }
}
#endif
