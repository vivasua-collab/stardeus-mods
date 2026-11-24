// #define DEBUG_TWITCH
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Systems.Twitch.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.CodeGen;
using System.Linq;
using Game.Systems.Creatures;
using Game.Input;
using Game.Systems.Mental;
using Game.Systems.Items;
using Game.Systems.UIUX;

namespace Game.Systems.Twitch {
    public sealed class TwitchSys : GameSystem {
        public const string SysId = "TwitchSys";
        public const string GlobalFileName = "Twitch.dat";
        private static string globalDataPath;
        private const int RepeatAnnounceAfterSeconds = 30 * 60;
        // hash of cmd -> factory
        private static readonly Dictionary<int, Func<TwitchCtx, TwitchCommand>> allCommands = new();
        // hash of cmd -> cmd
        public static readonly Dictionary<int, string> CommandMap = new();
        // hash of cmd -> help line
        public static readonly Dictionary<int, string> HelpLines = new();
        public static readonly Dictionary<int, int> CommandCosts = new();
        private static TwitchGlobalData globalData;
        public static bool IsActive => globalData != null && globalData.Config.IsActive;
        public TwitchGameData GameData;
        public TwitchSettings Settings;
        public TwitchHandler UI;
        public static TwitchViewerFlags[] AllFlags = new[] {
            TwitchViewerFlags.Viewer,
            TwitchViewerFlags.VIP,
            TwitchViewerFlags.Subscriber,
            TwitchViewerFlags.Moderator,
            TwitchViewerFlags.Broadcaster,
        };
        public static TwitchGlobalData GlobalData {
            get {
                if (globalData == null) {
                    LoadGlobalData(true);
                }
                return globalData;
            }
        }
        public TwitchViewerQueue Queue;
        public static void LoadGlobalData(bool createIfEmpty) {
            if (globalData != null) { return; }
            globalDataPath = Persist.PersistentPath(Consts.SaveDirectory, GlobalFileName);
            if (!SaveLoadUtils.TryDeserialize(globalDataPath, out globalData)) {
                if (createIfEmpty) {
                    globalData = new TwitchGlobalData();
                }
            } else if (globalData == null && createIfEmpty) {
                D.Err("Global Data is null after successful deserialize!");
                globalData = new TwitchGlobalData();
            }
            globalData?.AfterLoad();
        }
        private double lastAnnounceTime;
        private bool isInitialized;

        public bool IsInitialized => isInitialized;


        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TwitchSys());
            testTwitch = null;
            twitch = null;
        }
        private static TwitchClient twitch;
        public TwitchClient Client => twitch;
        private Dictionary<int, TwitchViewer> viewers;
        private bool isActive;
        public override string Id => SysId;
        protected override void OnInitialize() {
            #if UNITY_EDITOR
            if (The.TestsAreRunning || PlayerPrefs.GetInt("NoTwitch", 0) == 1) {
                IsDeactivated = true;
                return;
            }
            #endif
            LoadGlobalData(false);
            if (globalData == null || !globalData.Config.IsActive) {
                IsDeactivated = true;
            } else {
                foreach (var cost in globalData.CommandCosts) {
                    // overriding default command costs
                    CommandCosts[cost.Key] = cost.Value;
                }
                Settings = globalData.Settings;
                S.Sig.AfterLoadState.AddListener(OnLoadSave);
            }
        }

        public int CommandCost(int cmdH) {
            if (CommandCosts.TryGetValue(cmdH, out var cost)) {
                return cost;
            }
            D.Err("Cost not registered for twitch command with hash: {0}. All commands: {1}",
                cmdH, D.Dump(CommandMap));
            return 0;
        }

        private static TwitchClient testTwitch;

        public static void TestConfiguration(Action onEnd) {
            try {
                if (testTwitch != null) {
                    WPopup.Create(true, IconId.CWarning,
                        T.TwitchError,
                        T.TwitchErrorPreviousTestRunning)
                            .Spawn(UIPoolRoot.SystemPopupContainer);
                    return;
                }
                CleanupTest();
                testTwitch = new TwitchClient(GlobalData.Config, true);
                testTwitch.Settings = TwitchSettings.Default;
                testTwitch.ReconnectIfNeeded();
                Coroutines.Start(SendTestMessage(onEnd),
                    "TwitchSys", "SendTestMessage", false, true);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed testing twitch config");
                WPopup.Create(true, IconId.CWarning, T.TwitchError, ex.Message)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                CleanupTest();
                onEnd();
            }
        }

        public static void CleanupTest() {
            if (testTwitch != null) {
                testTwitch.Destroy();
                testTwitch = null;
            }
        }
        public void AssignBeing(TwitchViewer viewer, BeingData bd, bool rename) {
            Queue.TryRemove(viewer);
            Client.SendChannelMessage(
                "@{0} has joined the colony as a {1}!",
                    viewer.Name, bd.Species.Name);
            S.Sig.ShowFanfare.Send(string.Format("{2}<b>{0}</b> has joined the colony as <b>{1}</b>!",
                Texts.LinkToBeing(bd, viewer.Name), bd.Species.Name, GlyphMap.Sprite("Social", "Twitch")));
            GameData.BeingIds[viewer.Id] = bd.Id;
            if (rename) {
                RenameToViewer(viewer, bd);
            } else {
                UI.RebuildViewers();
                UI.RefreshViewer(viewer);
            }
        }

        public void RenameToViewer(TwitchViewer viewer, BeingData bd) {
            bd.Mind.Name.ParseName(viewer.Name);
            bd.UI.AfterRename();
            UI.RebuildViewers();
            UI.RefreshViewer(viewer);
        }

        private static IEnumerator SendTestMessage(Action onEnd) {
            var startWait = Time.realtimeSinceStartupAsDouble;
            while (!testTwitch.IsConnected || !testTwitch.IsConnectConfirmed) {
                if (testTwitch.HasAuthFailed) {
                    break;
                }
                if (Time.realtimeSinceStartupAsDouble - startWait > 10) {
                    D.Err("Could not connect to Twitch in 10 seconds");
                    WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch,
                        T.TwitchError,
                        T.TwitchErrorCouldNotConnect)
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                    break;
                }
                D.Log("State: {0}", testTwitch.State);
                yield return Consts.Wait100Ms;
            }
            if (testTwitch.HasAuthFailed) {
                TwitchClient.ShowAuthErrorPopup();
            } else if (testTwitch.IsConnected && testTwitch.IsConnectConfirmed) {
                try {
                    WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch,
                        T.TwitchSettings, T.TwitchTestMessageSent)
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                    D.Warn("Sending a test message to twitch");
                    testTwitch.SendSystemMessage("Stardeus Twitch integration is configured correctly!");
                } catch (Exception ex) {
                    WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch,
                        T.TwitchError, ex.Message)
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                }
            }
            yield return Consts.Wait100Ms;
            onEnd();
            CleanupTest();
        }

        public string CommandName(TwitchCommand cmd, bool forUI) {
            var c = TwitchCommand.IdToCmd(cmd.Id);
            if (CommandMap.TryGetValue(Hashes.S(c), out var name)) {
                return Settings.AddPrefix(name, forUI);
            }
            return cmd.Id;
        }

        private void SetupTwitch() {
            if (isInitialized) { return; }
            D.Warn("Will setup twitch: {0}", GetHashCode());
            if (!GameState.IsCurrent(S)) {
                return;
                // D.Err("Stale game state in twitch setup! {0}", S);
            }
            var config = GlobalData.Config;
            isActive = config.IsActive;
            if (!isActive) { return; }
            if (!config.IsValid) {
                isActive = false;
                WPopup.Create(true, IconId.CWarning, T.TwitchError, T.NotConfigured)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            }
            if (twitch == null) {
                twitch = new TwitchClient(config, false);
            }
            twitch.Settings = Settings;

            if (twitch.IsConfigured) {
                #if DEBUG_TWITCH
                D.Log("Twitch integration is live!");
                #endif
                S.Clock.OnUpdate60.AddListener(OnUpdate);
                S.Clock.OnTick.AddListener(OnTick);
                twitch.OnCommand.AddListener(OnCommand);
                twitch.OnPart.AddListener(OnPart);
                twitch.OnJoin.AddListener(OnJoin);
                twitch.OnOpen.AddListener(OnOpen);
                twitch.OnClose.AddListener(OnClose);
                S.Sig.MindInstalled.AddListener(OnMindInstalled);
                S.Sig.MindRemoved.AddListener(OnMindRemoved);
                Configure();
                ApplyAllRoleChanges();
            } else {
                #if DEBUG_TWITCH
                D.Warn("Twitch is not configured!");
                #endif
            }
        }

        private void OnMindRemoved(Mind mind, BeingData beingFrom, ObjData toPart) {
            // check if the being was assigned to anyone
            var viewerId = 0;
            foreach (var vb in GameData.BeingIds) {
                if (vb.Value == beingFrom.Id) {
                    // need to unassign the being and assign the mind
                    viewerId = vb.Key;
                    GameData.BeingIds.Remove(viewerId);
                    break;
                }
            }

            if (toPart != null && viewerId != 0) {
                if (GlobalData.Viewers.TryGetValue(viewerId, out var viewer)) {
                    if (GameData.MindIds == null) {
                        GameData.MindIds = new();
                    }
                    GameData.MindIds[viewerId] = toPart.Id;
                    Client.SendChannelMessage("@{0}'s mind has been removed", viewer.Name);
                }
            }
        }

        private void OnMindInstalled(Mind mind, ObjData partFrom, BeingData toBeing) {
            if (partFrom == null || GameData.MindIds == null) { return; }
            // check if the being was assigned to anyone
            var viewerId = 0;
            foreach (var vb in GameData.MindIds) {
                if (vb.Value == partFrom.Id) {
                    // need to unassign the being and assign the mind
                    viewerId = vb.Key;
                    GameData.MindIds.Remove(viewerId);
                    break;
                }
            }

            if (toBeing != null && viewerId != 0) {
                if (GlobalData.Viewers.TryGetValue(viewerId, out var viewer)) {
                    GameData.BeingIds[viewerId] = toBeing.Id;
                    Client.SendChannelMessage("@{0}'s mind has been inserted into {1}",
                        viewer.Name, toBeing.Species.Name);
                }
            }
        }

        private void OnOpen(string args) {
            UI.UpdateConnectionState(true);
        }
        private void OnClose(string args) {
            UI.UpdateConnectionState(true);
        }

        private void OnLoadSave(GameState state) {
            if (IsDeactivated) { return; }
            if (G.IsDemoMode) { return; }
            // D.Warn("Loaded save for {0}", GetHashCode());
            viewers = globalData.Viewers;
            GameData = globalData.GameDataFor(state, true);
            Queue = GameData.Queue;
            Ready.When(WhenH.BeingsLoaded, SetupTwitch);
            // We didn't run setup asyncronously
            if (isInitialized) {
                // Coroutines.Start(RejoinViewers(), this, "RejoinViewers");
            }
            ShowWelcomePopup();
        }

        private void ShowWelcomePopup() {
            if (GameData.SkipWelcome) { return; }
            Ready.WhenGameLate(this, "Twitch", () => {
                WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch, T.MenuSettingsTwitch,
                    "Twitch integration is activated! Use Quick Search or the Ship Computer app to open the Twitch control panel.")
                    .WithAction(T.Ok, null)
                    .WithAction2(T.DoNotShowAgain, () => {
                        GameData.SkipWelcome = true;
                    })
                    .Spawn();
            });
        }

        public void ViewerJoined(TwitchViewer viewer) {
            // D.Log("Viewer Joined: {0}", viewer.Name);
            viewer.IsViewing = true;
            viewer.LastSeen = DateTime.UtcNow;
            UI.RebuildViewers();
        }
        public void ViewerLeft(TwitchViewer viewer) {
            // D.Log("Viewer Left: {0}", viewer.Name);
            viewer.IsViewing = false;
            viewer.LastSeen = DateTime.UtcNow;
            UI.RebuildViewers();
        }

        private void OnJoin(string nick) {
            // D.Log("OnJoin: {0}", nick);
            var viewer = FindViewer(nick);
            if (viewer != null) {
                ViewerJoined(viewer);
            } else {
                CheckSendWelcome();
            }
        }

        private void CheckSendWelcome() {
            var gt = GameTimer.realtimeSinceStartupAsDouble;
            if (lastAnnounceTime == 0f) {
                lastAnnounceTime = gt;
                return;
            }
            if (gt - lastAnnounceTime > RepeatAnnounceAfterSeconds) {
                SendWelcomeMessage();
            }
        }

        private void OnPart(string nick) {
            // D.Log("OnPart: {0}", nick);
            var viewer = FindViewer(nick);
            if (viewer != null) {
                ViewerLeft(viewer);
            }
        }


        private void Configure() {
            twitch.ReconnectIfNeeded();
            SendWelcomeMessage();
            isInitialized = true;
        }

        private void SendWelcomeMessage() {
            lastAnnounceTime = GameTimer.realtimeSinceStartupAsDouble;
            var welcomeMsg = Settings.WelcomeMsg;
            if (string.IsNullOrWhiteSpace(welcomeMsg)) {
                welcomeMsg = $"Stardeus: use {Settings.AddPrefix("help", false)} to see commands";
            }
            twitch.SendSystemMessage(welcomeMsg);
        }

        private void OnUpdate() {
            twitch.ReconnectIfNeeded();
            UpdateReputation();
        }

        public void OnTick(long ticks) {
        }
        #if PRODUCTION_BUILD
        private const float UpdateFrequencySec = 60f;
        #else
        private const float UpdateFrequencySec = 1f;
        #endif
        private double nextRepUpdate;
        public bool UpdateReputation() {
            var rt = GameTimer.realtimeSinceStartupAsDouble;
            if (nextRepUpdate == 0) {
                nextRepUpdate = rt;
                return false;
            }
            if (nextRepUpdate <= rt) {
                nextRepUpdate = rt + UpdateFrequencySec;
                AddPeriodicReputation();
                return true;
            }
            return false;
        }

        private void AddPeriodicReputation() {
            var gain = Settings.RepGainPerHour;
            if (gain < 1) {
                return;
            }
            var repPerMinute = gain / 60f;
            var minutesPassed = UpdateFrequencySec / 60f;
            var addAmt = repPerMinute * minutesPassed;
            ApplyAllRoles(addAmt);
        }

        private void ApplyAllRoles(float addAmt) {
            foreach (var v in viewers.Values) {
                if (v.IsViewing) {
                    v.AddReputation(addAmt);
                    ApplyRoleChanges(v);
                }
            }
        }

        public void ApplyAllRoleChanges() {
            foreach (var v in viewers.Values) {
                ApplyRoleChanges(v);
            }
        }

        public void ApplyRoleChanges(TwitchViewer viewer) {
            bool changed = false;
            foreach (var role in Settings.Roles.Values) {
                if (role.UpdateFor(viewer)) { changed = true; }
            }
            if (changed) { viewer.NotifyViewerChanged(); }
        }
        // FIXME if first message comes in form of whisper, we don't get
        // anything about the user!
        public void SetInitialRoles(TwitchViewer viewer, TwitchMessage msg) {
            if (!msg.ParseViewerInfo(viewer)) {
                return;
            }
            ApplyRoleChanges(viewer);
        }

        private void OnCommand(TwitchMessage message) {
            // Have to enqueue commands here, otherwise it would not run
            // on main thread!
            try {
                if (!The.GameIsInitialized) { return; }
                var viewer = FindViewer(message.Username);
                var tick = G.Ticks;
                var cmd = ParseCommand(viewer, message);
                if (cmd != null) {
                    if (viewer != null) {
                        viewer.EnqueueCommand(S, tick, cmd);
                    } else {
                        S.CmdQ.Enqueue(cmd);
                    }
                }
                viewer?.MarkSeen(this);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed processing Twitch command join");
            }
        }

        private TwitchCommand ParseCommand(TwitchViewer viewer, TwitchMessage message) {
            // !help or !xhelp
            var cmd = message.Command;
            cmd = Settings.StripPrefix(cmd);
            var cmdH = Hashes.S(cmd);
            if (!Settings.IsCommandEnabled(cmdH)) {
                Client.ReplyTo(message, "{0} command is disabled", cmd);
                return null;
            }
            if (allCommands.TryGetValue(cmdH, out var act)) {
                return act(new TwitchCtx() {
                    Sys = this,
                    Msg = message,
                    Viewer = viewer,
                    RepCost = CommandCosts[cmdH]
                });
            } else {
                D.Warn("Unknown twitch command: {0}", cmd);
            }
            return null;
        }

        public TwitchViewer FindViewer(string name) {
            var id = Hashes.S(name.ToLowerInvariant());
            if (viewers.TryGetValue(id, out var viewer)) {
                return viewer;
            }
            return null;
        }

        /// <summary>
        /// Warning, this won't run in main thread!
        /// </summary>
        public TwitchViewer FindOrCreateViewer(TwitchMessage msg) {
            var v = FindViewer(msg.Username);
            if (v != null) { return v; }
            var viewer = new TwitchViewer();
            viewer.Name = msg.Username;
            viewer.Id = Hashes.S(viewer.Name.ToLowerInvariant());
            viewer.IsViewing = true;
            viewer.LastSeen = DateTime.UtcNow;
            SetInitialRoles(viewer, msg);
            viewers[viewer.Id] = viewer;
            UI.RebuildViewers();
            return viewer;
        }

        public override void Unload() {
            if (globalData != null) {
                SaveGlobalData();
                globalData.Cleanup();
            }
            isInitialized = false;
            if (twitch != null) {
                twitch.Destroy();
                twitch = null;
            }
            if (testTwitch != null) {
                testTwitch.Destroy();
                testTwitch = null;
            }
        }

        public static void RegisterCommand(string cmd,
                int defaultCost,
                Func<TwitchCtx, TwitchCommand> act, string helpLine) {
            var cmdH = Hashes.S(cmd);
            allCommands[cmdH] = act;
            CommandMap[cmdH] = cmd;
            CommandCosts[cmdH] = defaultCost;
            if (!string.IsNullOrWhiteSpace(helpLine)) {
                HelpLines[cmdH] = helpLine;
            }
        }


        public static void SaveGlobalData() {
            if (globalData == null) { return; }
            if (!SaveLoadUtils.TrySerialize(globalData, globalDataPath)) {
                D.Err("Failed saving twitch viewer data! {0}", globalDataPath);
            }
        }

        public void DeleteAllViewers() {
            globalData.Viewers?.Clear();
            viewers.Clear();
            Queue.Clear();
        }

        public BeingData BeingOf(TwitchViewer viewer) {
            if (viewer == null) {
                return null;
            }
            if (GameData.BeingIds.TryGetValue(viewer.Id, out var beingId)) {
                var being = S.Query.FindBeingData.Ask(beingId);
                if (being != null) {
                    return being;
                }
                // being disappeared
                D.Err("Being by id {0} linked to viewer {1} is no longer available! unlinking",
                    beingId, viewer.Name);
                GameData.BeingIds.Remove(viewer.Id);
            }
            return null;
        }
        public ObjData MindObjOf(TwitchViewer viewer) {
            if (viewer == null) {
                return null;
            }
            if (GameData.MindIds?.TryGetValue(viewer.Id, out var objId) == true) {
                var obj = S.Query.FindObjData.Ask(objId);
                if (obj != null && obj.Mind != null && obj.State != ObjState.Destroyed) {
                    return obj;
                }
                // mind disappeared
                D.Err("Mind by id {0} linked to viewer {1} is no longer available! unlinking",
                    objId, viewer.Name);
                GameData.MindIds.Remove(viewer.Id);
            }
            return null;
        }

        public void ResetAllPoints() {
            foreach (var v in globalData.Viewers) {
                v.Value.SetReputation(0);
            }
            foreach (var v in viewers) {
                v.Value.SetReputation(0);
            }
        }
    }
}
