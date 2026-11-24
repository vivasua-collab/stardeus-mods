#if UNITY_STANDALONE_OSX
// #define OSX_VIDEO_HACK
#endif
#if PRODUCTION_BUILD
#define VERBOSE_MODS
#else
// #define LOG_GFX_TIMING
// #define SKIP_MODS
// #define DEBUG_MODS
// #define STARDEUS_HOT_LOAD_CS
#endif
using KL.Utils;
using Game.Rendering;
using System.Collections;
using System.Collections.Generic;
using Game.Data;
using System;
using KL.Signals;
using Game.Components;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using System.IO;
using System.Threading.Tasks;
using Game.Research;
using Game.UI;
using MessagePack;
using Game.Commands;
using UnityEngine.CrashReportHandler;
using Game.Procgen;
using Game.Input;
using Game.Systems.Story;
using System.Text;
using Game.Systems.UIUX;
using Game.Platform;



#if !UNITY_XBOXONE
using System.Reflection;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
#endif
using System.Linq;


namespace Game.Mods {
    public class ModLoader {
        #if !PRODUCTION_BUILD
        private const int testTextureQuality = 1;
        #endif
        public const string BootBuildGFX1Key = "BootBuildGFX1";
        public const string BootBuildGFX2Key = "BootBuildGFX2";
        #if UNITY_EDITOR
        public const int MaxAtlasSize = 8192;
        // public const int MaxAtlasSize = 1024;
        #else
        public const int MaxAtlasSize = 8192;
        #endif

        private const string ModFolderName = "Mods";
        private ModDiscovery discovery;
        private JSONPatcher jsonPatcher;
        public readonly Dictionary<string, string> jsonErrors = new();
        public readonly Dictionary<Def, string> defErrors = new();
        public ModDiscovery Discovery => discovery;
        // Ordered mods that are loaded
        private readonly HashSet<ModInfo> loadedMods = new();
        private readonly HashSet<ModInfo> loadedDevDisabledMods = new();
        private readonly HashSet<ModInfo> loadedRuntimeBrokenMods = new();
        private readonly HashSet<string> availableMods = new();
        private string[] orderedLoadedMods;
        public Dictionary<string, ModInfo> ModInfos;
        private ModInfo coreMod;
        public HashSet<string> AvailableMods => availableMods;
        public HashSet<ModInfo> LoadedMods => loadedMods;

        private ModLoader(ModDiscovery d) {
            discovery = d;
            jsonPatcher = new JSONPatcher(this);
        }

        public static ModLoader Create() {
            var d = new ModDiscovery();
            d.AddRoot($"{Application.streamingAssetsPath}/{ModFolderName}", false);
            return new ModLoader(d);
        }

        // WHY at Load and not in Create: Create runs at SubsystemRegistration, before the
        // BeforeSplashScreen launch flags (PlaytestRun) point persistence at their own profile,
        // so a playtest player read the developer's real Mods folder
        private bool userRootAdded;
        private void AddUserModsRoot() {
            if (userRootAdded) { return; }
            userRootAdded = true;
            // The demo runs only the bundled Core and Translations: mods and shared ships are
            // verified against the full game only, and the demo is not the same game
            #if SKIP_MODS || STARDEUS_DEMO
            return;
            #else
                #if !PRODUCTION_BUILD
                    if (The.TestsAreRunning) { return; }
                    // only the folders the validation spec names (ModDiscovery.CollectMods)
                    if (ModValidationRun.IsActive) { return; }
                #endif
            // Mods just don't work well in Unity Editor
            discovery.AddRoot(Persist.PersistentPath(ModFolderName), true);
            #endif
        }

        private double loadStartTime;
        private static int spriteChecksum;
        private static Dictionary<string, (int checksum, bool minNumAtlases)> atlasMetadata;
        // Cached atlas pages loaded this boot, so the incremental path does not load them twice
        private readonly Dictionary<string, Texture2D> loadedCachedAtlases = new();
        // The raw cache stores pixels the way the GPU gets them
        private static bool WantsCompressedCache =>
            AtlasBuilder.UseRawAtlasData && Res.CompressesTextures;

        // For the editor tooling only
        public static ModLoader CoreLoader {
            get {
                var d = new ModDiscovery();
                var coreMod = $"{Application.streamingAssetsPath}/{ModFolderName}/Core";
                d.RegisterMod(coreMod);
                var ml = new ModLoader(d);
                //ml.availableMods.Add("Core");
                return ml;
            }
        }

        private static string ModEnabledKey(string modId) {
            return $"mod.enabled.{Consts.ModsVersionTag}.{modId}";
        }

        public static bool IsModEnabled(string id) {
            if (id == "Core") { return true; }
            var def = id == "Translations";
            return The.Prefs.GetBoolAutoHash(ModEnabledKey(id), def);
        }

        public static void ToggleMod(string id, bool enabled) {
            if (id == "Core") { return; }
            var key = ModEnabledKey(id);
            The.Prefs.SetBool(key, enabled);
        }
        public static bool ToggleMod(string id) {
            if (id == "Core") { return true; }
            var def = id == "Translations";
            var key = ModEnabledKey(id);
            var wasEnabled = The.Prefs.GetBoolAutoHash(key, def);
            var isEnabled = !wasEnabled;
            The.Prefs.SetBool(key, isEnabled);
            return isEnabled;
        }

        private string JsonFileName(string path, string dir) {
            path = path.Replace('\\', '/');
            dir = dir.Replace('\\', '/');
            var p = path.Split(dir, 2);
            path = p[^1];
            var i = path.LastIndexOf('.');
            if (i == -1) {
                return path;
            }
            // strip the extension
            return path[..i];
        }

        // generic path -> actual path
        // path -> config/twitch/roles
        // /mods/core/config/twitch/roles/play.json
        // /mods/core/config/twitch/roles/leave.json
        // /mods/foo/config/twitch/roles/leave.json
        // /mods/foo/config/twitch/roles/foo.json
        // ->
        // play -> /mods/core/config/twitch/roles/play.json
        // leave -> /mods/foo/config/twitch/roles/leave.json
        // foo -> /mods/foo/config/twitch/roles/foo.json
        public List<T> LoadObjects<T>(string path, bool cache = false) {
            if (orderedLoadedMods == null) {
                D.Err("Trying to call LoadObjects while orderedLadedMods is blank!");
                return new List<T>();
            }
            using (BootTimeline.Span(BootTimeline.IsActive ? $"Json {path}" : null)) {
                var dict = FindObjectFiles(path);
                var res = DeserializeObjects<T>(path, dict);
                return res;
            }
        }

        // The JSON files under path in every loaded mod, by path relative to it, a later mod's
        // file replacing an earlier one's at the same relative path (see LoadObjects)
        public Dictionary<string, string> FindObjectFiles(string path) {
            var dict = new Dictionary<string, string>();
            if (orderedLoadedMods == null) {
                D.Err("Trying to call FindObjectFiles while orderedLadedMods is blank!");
                return dict;
            }

            // MUST BE A FORWARD LOOP
            for (int i = 0; i < orderedLoadedMods.Length; i++) {
                try {
                    string mod = orderedLoadedMods[i];
                    var objectDir = ModFolder(mod, path);
                    // This mod doesn't contain any objects of this kind
                    if (!Directory.Exists(objectDir)) { continue; }
                    var objFiles = Res.ListFilesRecursive(objectDir, ".json");
                    for (int i1 = 0; i1 < objFiles.Length; i1++) {
                        string objFile = objFiles[i1];
                        var objPath = Files.CombinePath(objectDir, objFile);
                        dict[objFile] = objPath;
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed loading mod object: {0}", path);
                }
            }
            return dict;
        }

        public HashSet<string> LoadTextLines(string path) {
            var lines = new HashSet<string>();
            for (int i = orderedLoadedMods.Length - 1; i >= 0; i--) {
                string mod = orderedLoadedMods[i];
                var filePath = ModFile(mod, $"{path}.txt");
                // This mod doesn't have a file
                if (File.Exists(filePath)) {
                    foreach (var l in Files.ReadLines(filePath)) {
                        var line = l.Trim();
                        if (!string.IsNullOrWhiteSpace(line)) {
                            lines.Add(line);
                        }
                    }
                }
            }
            return lines;
        }


        #if !PRODUCTION_BUILD
        private static string defaultGraphicsCacheDir {
            get {
                #if UNITY_EDITOR
                if (Coroutines.IsRunningTests) {
                    return Persist.PersistentPath($"Cache_Editor_{Consts.DevVersion}_Test");
                }
                return Persist.PersistentPath($"Cache_Editor_{Consts.DevVersion}");
                #else
                if (Coroutines.IsRunningTests) {
                    // Application.streamingAssetsPath is used for PersistentPath and
                    // it points to temporary directory, so cache can never be
                    // reused
                    return $"{Application.persistentDataPath}/Cache_Debug_{Consts.DevVersion}_Test";
                }
                return Persist.PersistentPath($"Cache_Debug_{Consts.DevVersion}");
                #endif
            }
        }
        #else
        private static string defaultGraphicsCacheDir => Persist.PersistentPath("Cache");
        #endif
        public static string GraphicsCacheDir => GetGraphicsCacheDir();
        private static string finalGraphicsCacheDir;

        private static string GetGraphicsCacheDir() {
            if (finalGraphicsCacheDir != null) {
                return finalGraphicsCacheDir;
            }
            if (PlatformUtils.TryCommandLineArgValue("--graphics-cache-dir", out var dir)) {
                if (!Files.EnsureDir(dir)) {
                    WPopup.Create(true, null, "Invalid directory",
                        "Couldn't create a directory for graphics cache",
                            dir).Spawn(true, UIPoolRoot.SystemPopupContainer);

                    dir = defaultGraphicsCacheDir;
                } else {
                    D.Warn("Overriding graphics cache directory to: {0}", dir);
                }
            } else {
                dir = defaultGraphicsCacheDir;
            }
            finalGraphicsCacheDir = dir;

            return dir;
        }

        private void DecorateModInfo(ModInfo info, string modDir) {
            info.FullPath = modDir;
            if (string.IsNullOrWhiteSpace(info.Id)) {
                D.Err("ID not defined for mod {0}. Please add Id field in {1}.ModInfo.json",
                    info.Name, modDir);
                info.Id = info.Name;
            }
            if (string.IsNullOrWhiteSpace(info.ModVersion)) {
                info.ModVersion = "1.0.0";
            }
            if (info.ModVersion.Contains('*')) {
                D.Err(".* is not allowed in ModVersion! Use .0 instead!: {0}: {1}",
                    info.Id, info.ModVersion);
                info.ModVersion = info.ModVersion.Replace('*', '0');
            }
            var checkDisabled = true;
            #if !PRODUCTION_BUILD
            // the validation run retests listed mods: that is how an entry gets removed
            checkDisabled = !ModValidationRun.IsActive;
            #endif
            if (checkDisabled
                    && DisabledMods.IsDisabled(info.Id, info.ModVersion, out var obsoleteVersion)) {
                if (string.IsNullOrEmpty(obsoleteVersion)) {
                    D.Warn("Mod {0} is obsolete", info.Id);
                } else {
                    D.Warn("Mod {0} with version <= {1} is obsolete",
                        info.Id, obsoleteVersion);
                }
                info.IsDevDisabled = true;
            }
            /*
            if (info.IsObsolete || info.IsBroken) {
                info.IsEnabled = false;
            }
            */
            if (info.SteamWorkshopId > 0) {
                // Append workshopId to mod id
                info.Id = $"{info.Id}_{info.SteamWorkshopId}";
            }
        }
        #if UNITY_EDITOR
        private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args) {
            // The DLL name
            string dllName = new AssemblyName(args.Name).Name + ".dll";
            D.Warn("Trying to resolve dll: {0}", dllName);
            string dllPath = "path_to_your_dll"; // replace with your DLL path

            // Check if DLL is in the specified directory
            if (File.Exists(dllPath)) {
                return Assembly.LoadFrom(dllPath);
            }

            // If DLL is not found, return null
            return null;
        }
        #endif

        private string QualityName(int q) {
            switch (q) {
                case 0: return T.TexFull;
                case 1: return T.TexHalf;
                case 2: return T.TexQuarter;
                case 3: return T.PixelArt;
                case 4: return T.PixelArtLowBudget;
                default: return $"Unknown! {q}";
            }
        }

        public IEnumerator Load() {
            // wait for 2 frames for video to start play
            var waitStart = Time.realtimeSinceStartupAsDouble;
            yield return CheckCrashRecovery();
            #if PRODUCTION_BUILD
            D.Warn("Stardeus Production build");
            #else
            #if UNITY_EDITOR
            // D.Warn("Stardeus Unity Editor");
            #else
            D.Warn("Stardeus Development build");
            #endif
            #endif
            #if UNITY_EDITOR
            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
            #endif
            spriteChecksum = 17;
            atlasMetadata ??= new();
            atlasMetadata.Clear();
            DetectTextureSize();
            var start = loadStartTime = Time.realtimeSinceStartupAsDouble;
            Ready.SetMods(false, "Loading mods");
            Translations.Unload();
            Tunable.Reset();
            DisabledMods.Load();
            AddUserModsRoot();

            #if !DISABLESTEAMWORKS
            var refreshWorkshop = true;
            #if !PRODUCTION_BUILD
            // a validation run loads only its own folders, never the Steam user's subscriptions
            refreshWorkshop = !ModValidationRun.IsActive;
            #endif
            #if STARDEUS_DEMO
            // nor does the demo, which runs only the bundled Core and Translations
            refreshWorkshop = false;
            #endif
            CrashWatchdog.UpdateBoot("Steam Workshop: Start Refresh");
            if (refreshWorkshop) {
                using (BootTimeline.Span("Workshop")) {
                    yield return discovery.RefreshWorkshopMods();
                }
            }
            AdvanceStage();
            yield return null;
            CrashWatchdog.UpdateBoot("Steam Workshop: End Refresh");
            #endif

            var discoverySpan = BootTimeline.Span("Mod discovery");
            discovery.CollectMods();

            //var modDirs = Res.ListStreamingDirs(ModFolder);
            var modDirs = discovery.Available;
            ModInfos = new Dictionary<string, ModInfo>();
            loadedMods.Clear();
            orderedLoadedMods = null;
            availableMods.Clear();
            loadedDevDisabledMods.Clear();

            foreach (var modDir in modDirs) {
                //var modInfoPath = ModFilePath(mod, "ModInfo.json");
                var modInfoPath = $"{modDir}/ModInfo.json";
                if (!File.Exists(modInfoPath)) {
                    D.Err("Skipping mod folder without ModInfo.json: {0}", modDir);
                    WPopup.Create(true, null, "SKIPPING MOD", modDir, $"{modInfoPath} does not exist").Spawn(UIPoolRoot.SystemPopupContainer);
                    continue;
                }
                try {
                    if (!TryDeserializeModInfo(modDir, modInfoPath, out var info, out var error)) {
                        D.Err("ModInfo parsing failed for {0}: {1}", modInfoPath, error);
                        ModJsonErrorPopup.Show("SKIPPING MOD", ModJsonErrorPopup.DisplayPath(modDir),
                            ModJsonErrorPopup.FileLine(modInfoPath, error));
                        continue;
                    }
                    DecorateModInfo(info, modDir);

                    if (availableMods.Contains(info.Id)) {
                        var msg = string.Format("Mod with ID {0} is already loaded from {1}. Skipping {2}.<br><br>If you are the developer of this mod and have both local and steam versions, you can ignore this warning because the local version is loaded first, and steam version is ignored.<br><br>Warning: Disabling Steam version of the mod will also disable the local version since their IDs match!",
                            info.Id, Texts.Green(ModInfos[info.Id].FullPath), Texts.Red(modDir));
                        D.Err(msg);
                        WPopup.Create(true, null, "SKIPPING DUPLICATE MOD", info.Id, msg).Spawn(UIPoolRoot.SystemPopupContainer);
                        continue;
                    }
                    ModInfos[info.Id] = info;
                    availableMods.Add(info.Id);
                    if (info.Id == "Core") {
                        info.IsEnabled = true;
                        coreMod = info;
                    } else {
                        var def = info.Id == "Translations";
                        info.IsEnabled = The.Prefs.GetBoolAutoHash(
                            ModEnabledKey(info.Id), def);
                        #if !PRODUCTION_BUILD
                        // every mod the validation spec names is under test
                        if (ModValidationRun.IsActive) {
                            info.IsEnabled = true;
                        }
                        #endif
                    }
                } catch (Exception ex) {
                    D.LogEx(ex, $"ModInfo parsing failed for {modInfoPath}");
                    ModJsonErrorPopup.Show("SKIPPING MOD", ModJsonErrorPopup.DisplayPath(modDir),
                        ModJsonErrorPopup.FileLine(modInfoPath, ex.Message));
                }
            }

            jsonPatcher.CollectPatches();
            StartJsonPrefetch();
            discoverySpan.Dispose();

            HashSet<ModInfo> pendingMods = new();

            // 0. Load all DLL files first, respecting dependency order
            // This ensures that all types are available before any RuntimeInitializeOnLoadMethod calls
            CrashWatchdog.UpdateBoot("Loading DLL files for all mods");
            var dllLoadOrder = new List<ModInfo>();
            var remainingMods = new HashSet<ModInfo>(ModInfos.Values.Where(
                m => m.IsEnabled && !m.IsDevDisabled && !m.IsRuntimeBroken));

            // First, load mods with no dependencies or only Core dependency
            foreach (var mod in remainingMods.ToArray()) {
                var deps = mod.Dependencies;
                if (Arrays.IsEmpty(deps) || (deps.Length == 1 && deps[0] == "Core")) {
                    dllLoadOrder.Add(mod);
                    remainingMods.Remove(mod);
                }
            }

            // Then load remaining mods in dependency order
            int maxAttempts = remainingMods.Count + 1;
            while (remainingMods.Count > 0 && maxAttempts-- > 0) {
                foreach (var mod in remainingMods.ToArray()) {
                    bool hasAllDeps = true;
                    foreach (var dep in mod.Dependencies) {
                        if (!dllLoadOrder.Any(m => m.IdMatches(dep))) {
                            hasAllDeps = false;
                            break;
                        }
                    }
                    if (hasAllDeps) {
                        dllLoadOrder.Add(mod);
                        remainingMods.Remove(mod);
                    }
                }
            }

            // Load DLL files in dependency order
            var dllSpan = BootTimeline.Span("Mod DLLs");
            foreach (var mod in dllLoadOrder) {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning && mod.Id != "Core" && mod.Id != "Translations") {
                    continue;
                }
                #endif
                yield return LoadCodeIfSupported(mod.Id, mod);
                if (mod.IsRuntimeBroken) {
                    #if VERBOSE_MODS
                    D.Warn("Mod marked as runtime broken after DLL loading: {0}", mod.Id);
                    #endif
                    loadedRuntimeBrokenMods.Add(mod);
                }
            }
            dllSpan.Dispose();

            // 1. load mods that have no dependencies
            foreach (var mod in ModInfos) {
                if (!mod.Value.IsEnabled) {
                    #if VERBOSE_MODS
                    D.Warn("Skipping disabled mod: {0}", mod.Key);
                    #endif
                    continue;
                }
                if (mod.Value.IsDevDisabled) {
                    #if VERBOSE_MODS
                    D.Warn("Skipping dev disabled mod: {0}", mod.Key);
                    #endif
                    loadedDevDisabledMods.Add(mod.Value);
                    continue;
                }
                if (mod.Value.IsRuntimeBroken) {
                    #if VERBOSE_MODS
                    D.Err("Skipping runtime broken mod: {0}", mod.Key);
                    #endif
                    loadedRuntimeBrokenMods.Add(mod.Value);
                    continue;
                }
                var deps = mod.Value.Dependencies;
                if (Arrays.IsEmpty(deps) || (deps.Length == 1 && deps[0] == "Core")) {
                    #if VERBOSE_MODS
                    D.Warn("Loading mod with empty deps or dependant on core: {0}", mod.Key);
                    #endif
                    yield return LoadMod(mod.Key, mod.Value);
                    if (!mod.Value.IsRuntimeBroken) {
                        loadedMods.Add(mod.Value);
                    } else {
                        #if VERBOSE_MODS
                        D.Warn("Marking mod as runtime broken: {0}", mod.Key);
                        #endif
                        loadedRuntimeBrokenMods.Add(mod.Value);
                    }
                } else {
                    #if VERBOSE_MODS
                    D.Warn("Adding to pending mods (has unloaded dependencies): {0}", mod.Key);
                    #endif
                    pendingMods.Add(mod.Value);
                }
            }
            AdvanceStage();
            yield return null;

            using (BootTimeline.Span("Custom graphics")) {
                PreloadCustomGraphics(Persist.PersistentPath("Graphics"));
            }

            if (loadedMods.Count == 0) {
                WPopup.Create(true, null, "GAME BREAKING BUG", null,
                    "No mods were loaded. Try verifying game integrity in your Steam library").Spawn(UIPoolRoot.SystemPopupContainer);
                Ready.SetMods(true, "Done loading mods");
                #if VERBOSE_MODS
                D.Warn("Done loading mods");
                #endif
                yield break;
            }

            List<string> loadedModIds = new();

            int attempts = pendingMods.Count + 1;
            while (pendingMods.Count > 0 && attempts-- > 0) {
                #if VERBOSE_MODS
                D.Warn("Loading pending mods, attempts left: {0}: {1}", attempts,
                    string.Join(", ", pendingMods));
                #endif
                // Try to incrementally load all the mods that we need in order
                foreach (var mod in pendingMods.ToArray()) {
                    var modInfo = mod;
                    if (!modInfo.IsEnabled) {
                        #if VERBOSE_MODS
                        D.Warn("Skipping disabled pending mod: {0}", modInfo.Id);
                        #endif
                        continue;
                    }
                    if (modInfo.IsDevDisabled) {
                        #if VERBOSE_MODS
                        D.Warn("Skipping dev disabled pending mod: {0}", modInfo.Id);
                        #endif
                        loadedDevDisabledMods.Add(modInfo);
                        continue;
                    }
                    if (modInfo.IsRuntimeBroken) {
                        #if VERBOSE_MODS
                        D.Err("Skipping runtime broken pending mod: {0}", modInfo.Id);
                        #endif
                        loadedRuntimeBrokenMods.Add(modInfo);
                        continue;
                    }
                    if (Arrays.IsEmpty(modInfo.Dependencies)) {
                        #if VERBOSE_MODS
                        D.Warn("Skipping pending mod without dependencies: {0}", modInfo.Id);
                        #endif
                        continue;
                    }
                    if (loadedMods.Contains(mod)) { continue; }
                    bool hasAllDeps = true;
                    foreach (var dep in modInfo.Dependencies) {
                        if (!IsModLoaded(dep)) {
                            #if VERBOSE_MODS
                            D.Warn("Dependency for mod {0} not loaded: {1}", modInfo.Id, dep);
                            #endif
                            // some dependency still not loaded!
                            hasAllDeps = false;
                            break;
                        }
                    }
                    if (!hasAllDeps) {
                        #if VERBOSE_MODS
                        D.Err("Mod still has missing dependencies: {0}", modInfo.Id);
                        #endif
                        continue;
                    }
                    // All dependencies are loaded! OK to load
                    yield return LoadMod(mod.Id, modInfo);
                    if (!modInfo.IsRuntimeBroken) {
                        loadedMods.Add(mod);
                        pendingMods.Remove(mod);
                        var modId = modInfo.Id;
                        #if VERBOSE_MODS
                        D.Warn("Mod loaded: {0}", modInfo.Id);
                        #endif
                        if (modId != "Core" && modId != "Translations") {
                            loadedModIds.Add(modId);
                            CrashReportHandler.SetUserMetadata($"mod_{modId}",
                                modInfo.ModVersion ?? "0");
                            CrashReportHandler.SetUserMetadata("mod_count",
                                loadedModIds.Count.ToString());
                        }
                    } else {
                        #if VERBOSE_MODS
                        D.Err("Mod runtime broken: {0}", modInfo.Id);
                        #endif
                        loadedRuntimeBrokenMods.Add(modInfo);
                    }
                }
            }
            AdvanceStage();
            yield return null;

            if (pendingMods.Count > 0) {
                foreach (var mod in pendingMods) {
                    D.Err("Failed resolving some dependencies for mod: {0} ({1})",
                        mod, string.Join(", ", mod.Dependencies));
                }
            }

            orderedLoadedMods = loadedMods.Select(m => m.Id).ToArray();

            var locale = The.Prefs.GetString(Pref.GLanguage, PrefH.GLanguage,
                Translations.DefaultLocale);
            var userDataRoot = Persist.Root;
            var task = Task.Run(() => LoadTranslations(locale, userDataRoot));
            // Load graphics
            using (BootTimeline.Span("Graphics")) {
                yield return FinalizeLoadingGraphics();
            }
            AdvanceStage();
            LoadingMsg($"Processing Translations: {Translations.Count}");
            yield return null;

            using (BootTimeline.Span("Translations wait")) {
                while (!task.IsCompleted) {
                    yield return null;
                }
            }
            if (task.IsFaulted) {
                // Bootstrap.InitializeLocalization sets the language again on the main thread
                D.LogEx(task.Exception, "Failed loading translations on a worker");
            }
            AdvanceStage();
#if DEBUG_MODS
            var trEnd = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Translation processing took extra {0:0.00}s", trEnd - buildEnd);
#endif

            LoadingMsg("Core is ready");
            yield return null;
            // The.SysSig.LoadingMsg.Send(null);
            //#if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Loading all mods took {0:0.00}s", end - start);
            //#endif

            using (BootTimeline.Span("Materials")) {
                MatType.LoadKnown();
            }
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }
            using (BootTimeline.Span("Defs after load")) {
                The.Defs.AfterLoad();
            }
            if (FrameTimer.HasToYieldSlow()) {
                yield return null;
            }
            using (BootTimeline.Span("Defs validate")) {
                The.Defs.Validate();
            }
            using (BootTimeline.Span("Def icons")) {
                DefIcons.Rebuild();
            }

            foreach (var mod in loadedMods) {
                if (ModInfos.TryGetValue(mod.Id, out var info)) {
                    if (info.IsRuntimeBroken) {
                        loadedRuntimeBrokenMods.Add(info);
                    }
                }
            }

            var allMods = new List<ModInfo>(pendingMods);
            allMods.AddRange(loadedMods);

            foreach (var mod in allMods) {
                // check if any of the dependencies are broken
                if (Arrays.IsEmpty(mod.Dependencies)) { continue; }
                foreach (var dep in mod.Dependencies) {
                    var depInfo = FindModById(dep);
                    if (depInfo == null) {
                        mod.MarkRuntimeBroken($"Mod dependency not found: {dep}");
                        loadedRuntimeBrokenMods.Add(mod);
                    } else if (!depInfo.IsEnabled || depInfo.IsDevDisabled
                            || depInfo.IsRuntimeBroken) {
                        mod.MarkRuntimeBroken($"Mod dependency is disabled or broken: {dep}");
                    }
                }
            }

            Ready.SetMods(true, "Done loading mods");

            CheatMods.BlockAchievementsIfLoaded(loadedMods);

            if (loadedRuntimeBrokenMods.Count > 0) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                sb.Append(T.ModWarningRuntimeBroken);
                sb.Append(GlyphMap.BRHR);
                foreach (var mod in loadedRuntimeBrokenMods) {
                    sb.AppendFormat("\n{0}", ModName(mod, Texts.TMPColorRed));
                }
                var msg = sb.ToString();
                WPopup.Create(true, null, "MOD PROBLEMS", null, msg).Spawn(UIPoolRoot.SystemPopupContainer);
            }

            if (loadedDevDisabledMods.Count > 0) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                sb.Append(T.ModWarningDevDisabled);
                sb.Append("\n");
                foreach (var mod in loadedDevDisabledMods) {
                    sb.AppendFormat("\n{0}", ModName(mod));
                }
                sb.Append(GlyphMap.BRHR);
                sb.Append(Texts.GreySmaller("If you are the developer of any of these mods, change the mod version number when uploading a fix."));
                var msg = sb.ToString();
                WPopup.Create(true, null, "MOD PROBLEMS", null, msg).Spawn(UIPoolRoot.SystemPopupContainer);
            }
        }

        // English, which every language falls back to, then the player's language. Nothing
        // reads translations before Bootstrap.InitializeLocalization (T() logs an error when
        // something does), so the worker can parse the player's language too: there it took
        // 50-100 ms of one frame, a few times that on slow PCs.
        private static void LoadTranslations(string locale, string userDataRoot) {
            Translations.Initialize();
            if (!Translations.IsSupported(locale)) { return; }
            try {
                Translations.SetLocale(locale, true, userDataRoot);
            } catch (Exception) {
                // so InitializeLocalization loads it again on the main thread
                Translations.ForgetLocale();
                throw;
            }
        }

        // The JSON boot reads: the Config and Definitions folders of every mod that will load,
        // but not Config/Schemas, which only editors read. Each mod's load reads its tunables
        // and defs before any Config folder is read.
        private void StartJsonPrefetch() {
            var first = new List<string>();
            var rest = new List<string>();
            var skip = new List<string>();
            foreach (var info in ModInfos.Values) {
                if (!info.IsEnabled || info.IsDevDisabled || info.IsRuntimeBroken) { continue; }
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning && info.Id != "Core" && info.Id != "Translations") {
                    continue;
                }
                #endif
                foreach (var version in Consts.ModDirLoadOrder) {
                    var root = Path.Combine(info.FullPath, version);
                    first.Add(Path.Combine(root, "Config", "Tunable"));
                    first.Add(Path.Combine(root, "Definitions"));
                    rest.Add(Path.Combine(root, "Config"));
                    skip.Add(Path.Combine(root, "Config", "Schemas"));
                }
            }
            first.AddRange(rest);
            JsonPrefetch.Start(first, skip);
        }

        private IEnumerator CheckCrashRecovery() {
            yield return null;
            int lastGFXBootAttemptScale = CachedPlayerPrefs.GetInt(BootBuildGFX1Key, -1);
            int lastGFXBootAttemptAtlasSize = CachedPlayerPrefs.GetInt(BootBuildGFX2Key, -1);
            bool isWaitingForPlayer = false;
            if (lastGFXBootAttemptAtlasSize != -1 || lastGFXBootAttemptScale != -1) {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                Cursor.visible = true;
                isWaitingForPlayer = true;
                LoadingMsg("Waiting for player's choice...");
                WPopup.Create(true, null, "Boot Crash Recovery", null, string.Format(
                    "Last attempt to build the graphics cache didn't finish. If the game crashed, it could mean that there is not enough GPU memory to build the graphics at current quality and atlas size.<br><br>Last texture quality: <b>{0}</b>.<br>Last atlas size: <b>{1}</b>.<br><br>Would you like to reduce the graphics quality?<br><br><i>If you terminated the game process yourself, choose <b>No</b>.</i>",
                        QualityName(lastGFXBootAttemptScale), lastGFXBootAttemptAtlasSize))
                    .WithAction(T.Yes, () => {
                        // reduce quality
                        isWaitingForPlayer = false;
                        ReduceQuality(lastGFXBootAttemptScale, lastGFXBootAttemptAtlasSize);
                    })
                    .WithAction2(T.No, () => {
                        // just retry
                        isWaitingForPlayer = false;
                    })
                    .Spawn(center: true);

            }
            while (isWaitingForPlayer) {
                Cursor.visible = true;
                yield return null;
            }
            CachedPlayerPrefs.DeleteKey(BootBuildGFX1Key);
            CachedPlayerPrefs.DeleteKey(BootBuildGFX2Key);
            yield return null;
        }

        private void ReduceQuality(int q, int atlasSize) {
            if (q < 4) {
                CachedPlayerPrefs.SetInt(Res.TextureSizePref, q + 1);
            }
        }

        public ModInfo FindModById(string id) {
            if (ModInfos.TryGetValue(id, out var depInfo)) {
                return depInfo;
            }
            // here it could mean that we are referencing a workshop mod by
            // incomplete id, since workshop mods modify the id to be
            // $"{Id}_{WorkshopItemId}". Now we have to try finding a matching
            // mod:
            foreach (var mod in ModInfos.Values) {
                if (mod.IdMatches(id)) {
                    return mod;
                }
            }
            // not found!
            return null;
        }

        public bool IsModLoaded(string id) {
            foreach (var mod in loadedMods) {
                if (mod.IdMatches(id)) {
                    return true;
                }
            }
            return false;
        }

        private string spriteQualityText
            => QualityName(CachedPlayerPrefs.GetInt(Res.TextureSizePref, 0));

        public void SpriteLoadingMsg(int count) {
            The.ModLoader.LoadingMsg(
                $"Packing sprites (Quality: {spriteQualityText}): {count}/{RenderingService.Sprites.Count}. VRAM: {The.Alloc.AllocatedGPUMB}");
        }

        public void LoadingMsg(string msg) {
            The.SysSig.LoadingMsg.Send(msg);
            //D.Warn("Loading stage: {0}", msg);
        }

        public void AdvanceStage() {
            The.SysSig.AdvanceLoadingStage.Send();
        }

        private IEnumerator LoadMod(string mod, ModInfo info) {
            if (info.IsDevDisabled || info.IsRuntimeBroken) {
                D.Err("LoadMod was called for an obsolete or broken mod: {0}", info);
                yield break;
            }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning && mod != "Core" && mod != "Translations") {
                D.Warn("Skipping mod load in tests: {0}", mod);
                yield break;
            }
            #endif
            #if PRODUCTION_BUILD
            D.Warn("Loading Mod: {0}", info);
            #endif
            CrashWatchdog.UpdateBoot("Begin Load Mod: {0}", mod);
            // DLL loading was already done earlier in dependency order
            if (info.IsRuntimeBroken) { // DLL loading may have set this to true
                CrashWatchdog.UpdateBoot("Skip Broken Mod: {0}", mod);
                yield break;
            }
            if (info == coreMod) {
                yield return InitAsync(SelfInit.Stage.BeforeCoreDefs);
                yield return InitAsync(SelfInit.Stage.CoreDefs);
            }
            var tunablePath = ModFolder(mod, "Config/Tunable");
            using (BootTimeline.Span("Tunables")) {
                LoadTunables(mod, tunablePath, info);
            }
            LoadingMsg($"Loading {info.Name}: Graphics");
            yield return null;
            using (BootTimeline.Span("Graphics scan")) {
                PreloadGraphics(mod, info);
            }
            LoadingMsg($"Loading {info.Name}: Sounds");
            using (BootTimeline.Span("Sounds")) {
                yield return LoadSounds(mod, info);
            }
            LoadingMsg($"Loading {info.Name}: Definitions");
            yield return null;
            using (BootTimeline.Span("Defs")) {
                yield return LoadDefsAsync(info);
            }

            var translationsPath = info.ModFolder("Translations");
            if (Directory.Exists(translationsPath)) {
                if (mod == "Core") {
                    Translations.AddRootPath(translationsPath);
                } else if (info.IsCoreTranslation) {
                    Translations.AddCommunityPath(translationsPath);
                } else {
                    Translations.AddModPath(translationsPath);
                }
            }

            if (info == coreMod) {
                yield return InitAsync(SelfInit.Stage.AfterCoreDefs);
            }

            CrashWatchdog.UpdateBoot("End Load Mod: {0}", mod);
        }

        private IEnumerator LoadCodeIfSupported(string mod, ModInfo info) {
            #if UNITY_XBOXONE
            yield break;
            #endif
            #if STARDEUS_HOT_LOAD_CS
            yield return HotLoadCode(mod, info);
            #else
            CrashReportHandler.enableCaptureExceptions = false;
            yield return LoadCode(mod, info);
            Feedback.ApplyReportingChoice(The.Prefs.GetBool(Pref.GAutoReporting,
                PrefH.GAutoReporting, false));
            #endif
        }

        #if !UNITY_XBOXONE
        #if UNITY_EDITOR && STARDEUS_HOT_LOAD_CS
        private IEnumerator HotLoadCode(string mod, ModInfo info) {
        }
        #endif // UNITY_EDITOR

        private IEnumerator LoadCode(string mod, ModInfo info) {
            var start = Time.realtimeSinceStartupAsDouble;
            int fileCount = 0;
            if (!Arrays.IsEmpty(info.DLLFiles)) {
                fileCount = info.DLLFiles.Length;
                foreach (var file in info.DLLFiles) {
                    var dllFile = ModFile(mod, "Libraries", file);
                    if (!LoadDLLFile(info, dllFile)) {
                        yield break;
                    }
                }
            } else {
                var dllPath = ModFolder(mod, "Libraries");
                if (!Directory.Exists(dllPath)) { yield break; }
                var ids = Res.ListFilesRecursive(dllPath, ".dll");
                fileCount = ids.Length;
                for (int i = 0; i < ids.Length; i++) {
                    string id = ids[i];
                    var path = Files.CombinePath(dllPath, id);
                    if (!LoadDLLFile(info, path)) {
                        yield break;
                    }
                }
            }
            #if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            D.Warn("Mod loading code {0} ({2} files) took {1:0.00}s",
                mod, end - start, fileCount);
            #endif
        }

        private static bool LoadDLLFile(ModInfo info, string path) {
            bool isTemplate = info.Id.StartsWith("Template");
            D.Warn("Loading mod DLL file: {0}: {1}", info, path);
            try {
                if (!isTemplate && path.Contains("Stardeus_Mod_Template")) {
                    var err = string.Format("Mod {0} contains Stardeus_Mod_Template.dll file from the template project! Refusing to load this DLL file. There may be other errors, it is suggested to disable this mod until the issue is fixed.",
                        info.Id);
                    WPopup.Create(true, null, "MOD PROBLEM", null, err).Spawn(UIPoolRoot.SystemPopupContainer);
                    D.Err(err);
                    return false;
                }
                var assembly = Assembly.LoadFrom(path);
                if (assembly == null) {
                    #if DEBUG_MODS
                    D.Err("Could not load mod assembly from path: {0}", path);
                    #endif
                    return false;
                }
                #if !PRODUCTION_BUILD
                ModValidationRun.OnModAssemblyLoaded?.Invoke(info, assembly);
                #endif
                var types = assembly.GetTypes();
                #if DEBUG_MODS
                D.Warn("Got types from mod assembly: {0} -> {1}",
                    assembly, types.Length);
                #endif
                if (types.Length == 0) {
                    D.Err("Mod has a DLL file but it contains nothing. If you are the mod developer, consider removing the file: {0}", path);
                    // We will allow that, some mods have empty DLL files for whatever reason
                    //ModRegistry.AddBrokenMod(info, "DLL file has no types defined in it");
                }
                foreach (var type in types) {
                    #if DEBUG_MODS
                        D.Warn("Loading mod type: {0}", type);
                    #endif
                    ModRegistry.AddModType(type, info);
                    foreach (var meth in type.GetMethods(
                            BindingFlags.Static | BindingFlags.NonPublic)) {
                        var attrs = meth.GetCustomAttributes();
                        foreach (var attr in attrs) {
                            if (attr is RuntimeInitializeOnLoadMethodAttribute) {
                                try {
                                    meth.Invoke(null, null);
                                } catch (Exception ex) {
                                    D.LogEx(ex, "Failed invoking RuntimeInitialize for {0}. Mod: {1}",
                                        type, info);
                                    ModRegistry.MarkRuntimeBroken(info, $"Failed invoking RuntimeInitialize for type {type}");
                                    return false;
                                }
                            }
                        }
                    }
                }
                return true;
            } catch (Exception ex) {
                ModRegistry.MarkRuntimeBroken(info, $"Got runtime exception when loading code: {ex.Message}");
                D.LogEx(ex, "Failed loading assembly: {0}", path);
                return false;
            }
        }
        #endif

        private IEnumerator LoadSounds(string mod, ModInfo info) {
            var start = Time.realtimeSinceStartupAsDouble;
            var soundsPath = ModFolder(mod, "Sounds");
            if (!Directory.Exists(soundsPath)) { yield break; }
            var ids = Res.ListFilesRecursive(
                soundsPath, ".mp3", ".ogg", ".wav");
            var crts = new List<CoroutineItem>();
            CrashWatchdog.UpdateBoot("Loading Audio Clips: {0}", ids.Length);
            foreach (var id in ids) {
                var path = Files.CombinePath(soundsPath, id);
                crts.Add(Coroutines.Start(Res.LoadAudioClipAsync(path, (clip) => {
                    var clipId = Files.ToPathId(id);
                    #if DEBUG_MODS
                    // D.Log("Loading clip: {0} {1}", clipId, clip);
                    #endif
                    CrashWatchdog.UpdateBoot("Loaded audio clip: {0}", clipId);
                    The.Sounds.Set(clipId, clip);
                }), this, "LoadAudioClip", false, isSystem: true));
                while (Coroutines.ActiveCount > Consts.MaxLoadCoroutines) {
                        // || FrameTimer.HasToYieldSlow()) {
                    LoadingMsg($"Loading {info.Name}: Sounds {The.Sounds.Count}");
                    yield return null;
                }
            }
            yield return Coroutines.WaitForAll(crts, "LoadAudioClips");
            CrashWatchdog.UpdateBoot("Done Loading Audio Clips: {0}", ids.Length);
            #if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            D.Warn("Mod loading sounds {0} ({2} files) took {1:0.00}s",
                mod, end - start, ids.Length);
            #endif
        }

        public void LoadCoreTunables() {
            var path = CoreModFilePath("Config/Tunable");
            LoadTunables("Core", path, null);
        }

        public List<T> LoadCoreObjects<T>(string path) {
            var corePath = CoreModFilePath(path);
            var dict = new Dictionary<string, string>();

            var objFiles = Res.ListFilesRecursive(corePath, ".json");
            foreach (var objFile in objFiles) {
                var objPath = Files.CombinePath(corePath, objFile);
                dict[objFile] = objPath;
            }
            var res = DeserializeObjects<T>(path, dict);
            return res;
        }

        private List<T> DeserializeObjects<T>(string path, Dictionary<string, string> dict) {
            // Dict now contains all the paths with correct order overrides
            var res = new List<T>();
            var idField = typeof(T).GetField("Id",
                BindingFlags.Public | BindingFlags.Instance);
            foreach (var objPath in dict.Values.OrderBy(v => v)) {
                #if DEBUG_MODS
                // D.Log("Loading {0} from mod {1}", path, objPath);
                #endif
                // Some garbage files start with dot on osx for some reason
                if (Path.GetFileName(objPath).StartsWith(".")) {
                    D.Warn("Skipping file due to dot in filename: {0}", objPath);
                    continue;
                }
                if (Json.TryDeserialize(LoadPatchedJson(objPath), out T obj, out var err)) {
                    if (obj == null) {
                        err = "Null JSON object";
                    } else {
                        // Auto set ID where appropriate
                        if (idField != null && idField.FieldType == typeof(string)) {
                            string curVal = (string) idField.GetValue(obj);
                            if (string.IsNullOrEmpty(curVal)) {
                                var id = JsonFileName(objPath, path).Trim('/');
                                idField.SetValue(obj, id);
                            }
                        }
                        res.Add(obj);
                    }
                }
                if (err != null) {
                    jsonErrors[Files.CleanPath(objPath)] = err;
                }
            }

            if (jsonErrors.Count > 0) {
                D.Err("{0} Json errors after trying to load {1}",
                    jsonErrors.Count, path);
            }

            return res;
        }

        public void CheckErrors() {
            if (jsonErrors.Count == 0) { return; }
            bool hasCoreError = false;
            var keys = jsonErrors.Keys.ToArray();
            foreach (var objPath in keys) {
                if (ModJsonErrorPopup.IsCore(objPath)) {
                    hasCoreError = true;
                    break;
                }
            }
            var sb = Caches.StringBuilder3;

            sb.Clear();
            string title;
            if (hasCoreError) {
                title = "Installation Corrupted!";
                sb.Append(CoreErrorHint);
            } else {
                title = ModErrorTitle;
                sb.Append(ModJsonErrorPopup.ModHint);
            }
            sb.Append(GlyphMap.BRHR);
            sb.AppendFormat("JSON errors: <b>{0}</b>\n", jsonErrors.Count);
            int cnt = 0;
            foreach (var err in jsonErrors) {
                D.Err("JSON Error: {0}->{1}", err.Key, err.Value);
                sb.AppendFormat("<size=85%>{0}</size>\n\n",
                    ModJsonErrorPopup.FileLine(err.Key, err.Value));
                if (cnt++ > 3) { break; }
            }
            var msg = sb.ToString();
            jsonErrors.Clear();
            D.Err(msg);
            ModJsonErrorPopup.Show(title, null, msg);
        }

        private const string ModErrorTitle = "Mod Content Corrupted!";
        public const string CoreErrorHint = "There was an error while loading core content. Please try using <b>Local Files</b> > <b>Verify integrity of game files</b> in Stardeus <b>Steam Properties</b> and check if an anti-virus is not blocking the game from reading its own files.";

        // One popup per file that failed to load on its own (defs, tunables)
        private static void ShowFileError(string path, string error) {
            var isCore = ModJsonErrorPopup.IsCore(path);
            var title = isCore ? "Possible Game Breaking Bug!" : ModErrorTitle;
            var hint = isCore ? CoreErrorHint : ModJsonErrorPopup.ModHint;
            ModJsonErrorPopup.Show(title, null,
                $"{hint}{GlyphMap.BRHR}{ModJsonErrorPopup.FileLine(path, error)}");
        }

        public string LoadPatchedJson(string path) {
            #if UNITY_EDITOR
            // for generating tunable keys
            if (A.IsMainThread || A.MainThread == null) {
                if (!UnityEditor.EditorApplication.isPlaying) {
                    return Files.ReadJSONWithComments(path);
                }
            }
            #endif
            if (path == null) {
                D.Err("Trying to load JSON with null path!");
                return null;
            }
            path = jsonPatcher.GetPatched(path);
            // A patched file is a new file, so it never comes from the prefetch
            if (JsonPrefetch.TryTake(path, out var prefetched)) {
                return prefetched;
            }
            return Files.ReadJSONWithComments(path);
        }

        public void LoadTunables(string mod, string path, ModInfo info) {
            var start = Time.realtimeSinceStartupAsDouble;
            if (!Directory.Exists(path)) { return; }
            var files = Res.ListFilesRecursive(path, ".json");
            foreach (var file in files) {
                // .Override.json files are special, we load them manually when needed
                if (file.EndsWith(".Override.json")) {
                    continue;
                }
                var p = Files.CombinePath(path, file);
                #if !STARDEUS_DEMO
                var overridePath = p.Replace(".json", ".Override.json");
                if (File.Exists(overridePath)) {
                    // D.Warn("Overriding {0} with Override.json version", file);
                    p = overridePath;
                }
                #endif
                var json = LoadPatchedJson(p);
                if (!Json.TryDeserialize<Tunable>(json, file, out var t, out var error)) {
                    info.MarkRuntimeBroken($"Failed deserializing json file: {file}");
                    ShowFileError(p, error);
                }
                Tunable.Load(p, t);
            }
            #if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            D.Warn("Mod loaded tunables {0} ({2} files) took {1:0.00}s",
                mod, end - start, files.Length);
            #endif
        }

        public static int MaxTextureSize {
            get {
                var gpumem = SystemInfo.graphicsMemorySize;
                var maxTexSize = SystemInfo.maxTextureSize;
                if (maxTexSize <= 2048 || gpumem < 2000) {
                    return 2;
                } else if (maxTexSize <= 2048 * 2 || gpumem < 4000) {
                    return 1;
                }
                return 0;
            }
        }

        public static void DetectTextureSize() {
            var s = CachedPlayerPrefs.GetInt(Res.TextureSizePref, -1);
            // Let's default texture sizes to 0 if they're not set
            if (s < 0) {
                s = 0;
                CachedPlayerPrefs.SetInt(Res.TextureSizePref, s);
            }
            var finalSize = Video.GetRecommendedTextureSize(s);
            CachedPlayerPrefs.SetInt(Res.TextureSizePref, finalSize);
            Res.PixelsPerUnit = Res.basePixelsPerUnit / (1 + finalSize);
            // 1x1 sprite is 256x256 px
            var refScale = 256;
            if (finalSize > 0) {
                refScale /= finalSize;
            }
            RenderUtils.TextureQuality = finalSize;
            RenderUtils.TextureUnitSize = refScale;
            CrashWatchdog.UpdateBoot("Final Texture Size: {0}", finalSize);
            #if !UNITY_EDITOR
            D.Warn("Final Texture Size: {0}", finalSize);
            #endif
        }

        private void PreloadCustomGraphics(string rootPath) {
            CrashWatchdog.UpdateBoot("Begin Preload Custom Graphics");
            var start = Time.realtimeSinceStartupAsDouble;
            if (!Directory.Exists(rootPath)) { return; }
            var files = Res.ListFilesRecursive(
                rootPath, ".jpg", ".jpeg", ".png");
            for (int i = 0; i < files.Length; i++) {
                string id = files[i];
                PreloadCustomTexture(id, rootPath);
            }
            #if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            D.Warn("Mod preloading custom graphics {0} ({2} files) took {1:0.00}s",
                rootPath, end - start, files.Length);
            #endif
            CrashWatchdog.UpdateBoot("End Preload Custom Graphics");
        }

        private void PreloadGraphics(string mod, ModInfo info) {
            var start = Time.realtimeSinceStartupAsDouble;
            var graphicsPath = ModFolder(mod, "Graphics");
            if (!Directory.Exists(graphicsPath)) { return; }
            var files = Res.ListFilesRecursive(
                graphicsPath, ".jpg", ".jpeg", ".png");
            for (int i = 0; i < files.Length; i++) {
                string id = files[i];
                try {
                    PreloadOneTexture(id, graphicsPath, info);
                } catch (System.Exception ex) {
                    info.MarkRuntimeBroken($"Failed preloading texture: {graphicsPath}");
                    D.LogEx(ex, "Failed PreloadOneTexture for {0}: {1}: {2}",
                        mod, id, graphicsPath);
                }
            }
            #if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Mod preloading graphics {0} ({2} files) took {1:0.00}s",
                // mod, end - start, files.Length);
            #endif
        }

        private void WarnAboutGraphicsLoadErrors() {
            if (Arrays.IsEmpty(AtlasBuilder.Errors)) { return; }
            if (AtlasBuilder.HasCriticalError) {
                WPopup.Create(true, null, "ERROR", null, "Could not create or load graphics cache. Make sure you have enough free disk space, graphics cache folder is writable, and that anti-virus software did not block the file write access to the game.")
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                WPopup.Create(true, null, "ERRORS", null, string.Join("\n - ", AtlasBuilder.Errors))
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            } else {
                WPopup.Create(true, null, "ERROR", null,
                        "There were errors while loading graphics. The game might still be playable.")
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                WPopup.Create(true, null, "ERRORS", null, string.Join("\n - ", AtlasBuilder.Errors))
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            }
        }

        private IEnumerator FinalizeLoadingGraphics() {
            LoadingMsg("Loading graphics");
            yield return null;
            loadedCachedAtlases.Clear();
            var scale = CachedPlayerPrefs.GetInt(Res.TextureSizePref, 0);

            CachedPlayerPrefs.SetInt(BootBuildGFX1Key, scale);
            CrashWatchdog.UpdateBoot("Begin Finalize Graphics");

            bool loadedCache = false;
            GraphicsCacheData oldCache = null;
            // #if !PRODUCTION_BUILD
            // D.Warn("Trying to load graphics cache. Tests: {0}. Dir: {1}",
            //     Coroutines.IsRunningTests, GraphicsCacheDir);
            // #endif
            var startTime = Time.realtimeSinceStartupAsDouble;
            using (BootTimeline.Span("Atlas cache")) {
                yield return TryLoadGraphicsCached((cache, loaded) => {
                    loadedCache = loaded;
                    oldCache = cache;
                });
            }
            var endTime = Time.realtimeSinceStartupAsDouble;
            #if LOG_GFX_TIMING
            D.Warn("TryLoadGraphicsCached: {0:0.00}s", endTime - startTime);
            #endif
            BootTimeline.Note(string.Format("Graphics cache: {0}, {1}, {2}, quality {3}",
                AtlasBuilder.UseRawAtlasData ? "raw" : "png",
                Res.CompressesTextures ? "compressed" : "uncompressed",
                loadedCache ? "loaded" : oldCache != null ? "incremental rebuild" : "full rebuild",
                scale));
            startTime = Time.realtimeSinceStartupAsDouble;
            using (BootTimeline.Span("Raw textures")) {
                yield return LoadRawTextures();
            }
            AdvanceStage();
            yield return null;
            endTime = Time.realtimeSinceStartupAsDouble;
            #if LOG_GFX_TIMING
            D.Warn("LoadRawTextures: {0:0.00}s", endTime - startTime);
            #endif
            if (AtlasBuilder.HasCriticalError) {
                loadedCache = false;
                DeleteCacheDir();
            }
            if (loadedCache) {
                CachedPlayerPrefs.DeleteKey(BootBuildGFX1Key);
                var cachedMB = The.Alloc.AllocatedGPUMB;
                var cachedTextures = The.Alloc.AllocatedTextures;
                #if PRODUCTION_BUILD
                D.Warn("Finalizing graphics load (cached): {0} textures, VRAM: {1}",
                    cachedTextures, cachedMB);
                #endif
                yield break;
            }

            try {
                Files.EnsureDir(GraphicsCacheDir);
                File.WriteAllText($"{GraphicsCacheDir}/Readme.txt",
                    "You can change the location of Stardeus graphics cache directory by running Stardeus executable with this parameter:\n --graphics-cache-dir <full_path>\n\nIf you want to debug how the atlases look for modding purposes, or to reduce their size on disk, you can force PNG format (will load slower!):\n --force-png-atlas\n\nParameters can be defined in Steam's game properties -> Launch Options.");
            } catch (Exception ex) {
                D.LogEx(ex, "Failed writing Readme.txt!");
            }

            if (oldCache != null) {
                startTime = Time.realtimeSinceStartupAsDouble;
                var keptSpan = BootTimeline.Span("Kept atlases");
                // incrementally check and rebuild only the atlases that need it
                var rebuild = new HashSet<string>();
                foreach (var atlas in oldCache.Atlases) {
                    if (!atlasMetadata.TryGetValue(atlas.Info.Id, out var atlasMeta)) {
                        D.Warn("Removing old cached atlas: {0} (no checksum)", atlas.Id);
                        rebuild.Add(atlas.Info.Id);
                    } else if (atlas.SpriteChecksum != atlasMeta.checksum) {
                        D.Warn("Removing old cached atlas: {0} (checksum mismatch: {1} != {2})",
                            atlas.Id, atlasMeta.checksum, atlas.SpriteChecksum);
                        rebuild.Add(atlas.Info.Id);
                    }
                }
                DropCachedAtlases(oldCache, rebuild);
                var toLoad = new List<CachedAtlas>();
                foreach (var atlas in oldCache.Atlases) {
                    if (loadedCachedAtlases.ContainsKey(atlas.Id)) {
                        D.Warn("Keeping old cache for atlas: {0} (already loaded)", atlas.Id);
                    } else {
                        toLoad.Add(atlas);
                    }
                }
                var failed = new HashSet<string>();
                yield return CachedAtlasLoader.Load(toLoad, GraphicsCacheDir,
                    onLoaded: (atlas, tex) => {
                        loadedCachedAtlases[atlas.Id] = tex;
                        D.Warn("Keeping old cache for atlas: {0}", atlas.Id);
                    },
                    onFailed: atlas => {
                        D.Warn("Removing old cached atlas: {0} (load failure)", atlas.Id);
                        failed.Add(atlas.Info.Id);
                    });
                DropCachedAtlases(oldCache, failed);
                keptSpan.Dispose();
                endTime = Time.realtimeSinceStartupAsDouble;
                #if LOG_GFX_TIMING
                D.Warn("OldCacheValidation: {0:0.00}s", endTime - startTime);
                #endif
            }

            #if !PRODUCTION_BUILD
            D.Warn("Building graphics cache. Tests: {0}. Dir: {1}",
                Coroutines.IsRunningTests, GraphicsCacheDir);
            #endif
            LoadingMsg("Building graphics cache");
            yield return null;
            startTime = Time.realtimeSinceStartupAsDouble;
            var decodeSpan = BootTimeline.Span("Sprite decode");
            var toPrepare = new List<PreloadedAtlas>();
            foreach (var atlas in RenderingService.Sprites.ToLoadAtlas) {
                if (!IsAtlasCached(oldCache, atlas.Key)) {
                    if (oldCache != null) {
                        D.Warn("Will rebuild atlas since it was not in old cache: {0}. Old cache: {1}", atlas.Key,
                        string.Join(", ", oldCache.Atlases.Select(a => a.Info.Id)));
                    }
                    toPrepare.Add(atlas.Value);
                } else {
                    D.Warn("Skipping build of cached atlas: {0}", atlas.Key);
                }
            }
            yield return PrepareAtlasesForPacking(toPrepare);
            decodeSpan.Dispose();
            AdvanceStage();
            yield return null;
            endTime = Time.realtimeSinceStartupAsDouble;
            #if LOG_GFX_TIMING
            D.Warn("Loading Atlases: {0:0.00}s", endTime - startTime);
            #endif
            var buildStart = Time.realtimeSinceStartupAsDouble;
            LoadingMsg($"Packing {RenderingService.Sprites.Count} Sprites (scale:{CachedPlayerPrefs.GetInt(Res.TextureSizePref)})");
            yield return null;

            GraphicsCacheData cache = null;
            if (Consts.CacheGraphics) {
                if (!Files.EnsureDir(GraphicsCacheDir)) {
                    AtlasBuilder.AddError($"Could not create graphics cache directory: {GraphicsCacheDir}", false);
                } else {
                    cache = new GraphicsCacheData();
                    cache.Atlases = new List<CachedAtlas>();
                    if (oldCache != null) {
                        foreach (var a in oldCache.Atlases) {
                            D.Warn("Transferring old cached atlas to new cache: {0}", a.Id);
                            cache.Atlases.Add(a);
                        }
                    }
                    cache.Version = Consts.GraphicsCacheVersion;
                    cache.SpriteChecksum = spriteChecksum;
                }
            }

            startTime = Time.realtimeSinceStartupAsDouble;

            var maxAtlasSize = SystemInfo.maxTextureSize;
            var minAtlasSize = 4096;
            maxAtlasSize = Maths.ClosestPOT(maxAtlasSize, 2048, MaxAtlasSize);
            if (Consts.IsLessAwesomeGPU) {
                var maxAllowedSize = scale == 0 ? 4096 : 2048;
                if (maxAtlasSize > maxAllowedSize) {
                    // Let's try smaller atlas for low mem systems, to be sure
                    // it won't crash while trying to build them
                    maxAtlasSize = maxAllowedSize;
                }
            }
            if (minAtlasSize > maxAtlasSize) {
                minAtlasSize = maxAtlasSize;
            }
            CachedPlayerPrefs.SetInt(BootBuildGFX2Key, maxAtlasSize);

            using (BootTimeline.Span("Atlas build")) {
                yield return RenderingService.Sprites.Build(GraphicsCacheDir, cache,
                    maxAtlasSize, minAtlasSize, atlasMetadata);
            }

            CachedPlayerPrefs.DeleteKey(BootBuildGFX2Key);
            CachedPlayerPrefs.DeleteKey(BootBuildGFX1Key);

            endTime = Time.realtimeSinceStartupAsDouble;
            D.Warn("Built sprite atlases with quality: {0} in {1:0.0}s",
                RenderUtils.TextureQuality, endTime - startTime);

            if (cache != null) {
                cache.TextureQuality = CachedPlayerPrefs.GetInt(Res.TextureSizePref, -1);
                cache.Compressed = WantsCompressedCache;
                var sCache = SaveLoadUtils.Serialize(cache);
                var writeCacheResult = Persist.WriteSafe(GraphicsCacheDir, AtlasBuilder.CacheDataFileName, sCache);
                if (!writeCacheResult.IsSuccess) {
                    AtlasBuilder.AddError(
                        $"Could not write graphics {AtlasBuilder.CacheDataFileName} file in: {GraphicsCacheDir}",
                        false);
                }
            }

            WarnAboutGraphicsLoadErrors();

            var finalMB = The.Alloc.AllocatedGPUMB;
            var finalTextures = The.Alloc.AllocatedTextures;
            D.Warn("Finalizing graphics load: {0} textures, VRAM: {1}",
                finalTextures, finalMB);

            #if DEBUG_MODS
            var buildEnd = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Building all atlasses took {0:0.00}s", buildEnd - buildStart);
            #endif
        }

        private bool IsAtlasCached(GraphicsCacheData cache, string atlasId) {
            if (cache == null || cache.Atlases == null) { return false; }
            foreach (var a in cache.Atlases) {
                if (a.Info.Id == atlasId) {
                    return true;
                }
            }
            return false;
        }

        private IEnumerator LoadRawTextures() {
            yield return RawTextureCache.Load(GraphicsCacheDir,
                RenderingService.Sprites.ToLoadRaw);
        }

        public static bool HasRawAtlasData {
            get {
                var rawDataPath = $"{GraphicsCacheDir}/{AtlasBuilder.RawCacheDataFileName}";
                return File.Exists(rawDataPath);
            }
        }

        private IEnumerator TryLoadGraphicsCached(Action<GraphicsCacheData, bool> onDone) {
            if (!Consts.CacheGraphics) {
                onDone(null, false);
                yield break;
            }
            // Verify metadata
            var cachePath = $"{GraphicsCacheDir}/{AtlasBuilder.CacheDataFileName}";
            if (!File.Exists(cachePath)) {
                onDone(null, false);
                yield break;
            }
            GraphicsCacheData cache = null;
            try {

                if (!AtlasBuilder.UseRawAtlasData) {
                    // recover disk space by removing .dat atlases
                    foreach (var file in Directory.GetFiles(GraphicsCacheDir, "*.dat")) {
                        D.Warn("Deleting dat atlas since we'll be using PNGs: {0}", file);
                        Files.Delete(file);
                    }
                }

                CrashWatchdog.UpdateBoot("Begin Loading Graphics Cache");
                var res = Persist.ReadBytes(cachePath, out var bytes);
                if (!res.IsSuccess) {
                    onDone(null, false);
                    yield break;
                }
                if (bytes.Length < 1000) {
                    D.Err("Graphics Cache too short: {0} bytes", bytes.Length);
                    onDone(null, false);
                    yield break;
                }
                try {
                    cache = SaveLoadUtils.Deserialize<GraphicsCacheData>(bytes, false);
                } catch (MessagePackSerializationException sex) {
                    D.Err("Graphics Cache deserialization failed: {0}", sex.Message);
                    onDone(null, false);
                    yield break;
                }
                // Before the incremental check: kept pages must hold the wanted format
                if (cache.Compressed != WantsCompressedCache) {
                    D.Warn("Texture compression triggered graphics cache rebuild: Was {0} -> Wants {1}",
                        cache.Compressed, WantsCompressedCache);
                    DeleteCacheDir();
                    onDone(null, false);
                    yield break;
                }
                if (cache.SpriteChecksum != spriteChecksum) {
                    // we will incrementally rebuild only the atlasses that need rebuilding
                    // DeleteCacheDir();
                    D.Warn("Sprite checksum triggered incremental graphics cache rebuild");
                    onDone(cache, false);
                    yield break;
                }
                if (cache.Version != Consts.GraphicsCacheVersion) {
                    D.Warn("Graphics cache version triggered graphics cache rebuild");
                    DeleteCacheDir();
                    onDone(null, false);
                    yield break;
                }
                var texSize = CachedPlayerPrefs.GetInt(Res.TextureSizePref, -1);
                if (cache.TextureQuality != texSize) {
                    #if !PRODUCTION_BUILD
                    // Don't rebuild test cache if quality is different!
                    if (The.TestsAreRunning) {
                        CachedPlayerPrefs.SetInt(Res.TextureSizePref, cache.TextureQuality);
                    } else {
                        D.Warn("Texture quality triggered graphics cache rebuild: Was {0} -> Wants {1}",
                            cache.TextureQuality, texSize);
                        DeleteCacheDir();
                        onDone(null, false);
                        yield break;
                    }
                    #else
                    D.Warn("Texture quality triggered graphics cache rebuild: Was {0} -> Wants {1}",
                        cache.TextureQuality, texSize);
                    DeleteCacheDir();
                    onDone(null, false);
                    yield break;
                    #endif
                }
                if (!CheckCache(cache)) {
                    // DeleteCacheDir();
                    onDone(cache, false);
                    yield break;
                }
                LoadingMsg($"Loading graphics cache (Quality: {spriteQualityText})");
                CrashWatchdog.UpdateBoot("End Loading Graphics Cache");
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading cache");
                onDone(null, false);
                yield break;
            }
            yield return LoadCacheParallel(cache, onDone);
        }

        private static void DeleteCacheDir() {
            try {
                Files.ClearDirectory(GraphicsCacheDir, true);
                if (Directory.Exists(GraphicsCacheDir)) {
                    WPopup.Create(true, null, "ERROR", null,
                            $"Could not delete Graphics Cache Directory: {GraphicsCacheDir}\n\nMake sure the directory or files in it are not open and have read/write permissions!")
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                    CrashWatchdog.UpdateBoot("Failed deleting graphics cache dir");
                    D.Err("Failed deleting graphics cache directory! {0}", GraphicsCacheDir);
                }
            } catch (Exception ex) {
                CrashWatchdog.UpdateBoot(
                    "Failed deleting graphics cache dir: " + ex.Message);
                D.LogEx(ex, "Failed deleting graphics cache dir: {0}", GraphicsCacheDir);
            }
        }

        private bool CheckCache(GraphicsCacheData cache) {
            foreach (var atlas in cache.Atlases) {
                var path = AtlasBuilder.AtlasPath(GraphicsCacheDir, atlas.Id);
                if (!File.Exists(path)) {
                    var msg = $"Graphics cache could not find the file: {path}";
                    D.Err(msg);
                    CrashWatchdog.UpdateBoot(msg);
                    // force rebuild atlas
                    atlas.SpriteChecksum = -1;
                    return false;
                }
                var checksum = Sprites.Checksum(atlas.Id, path, null);
                if (atlas.AtlasSpriteChecksum != checksum) {
                    atlas.SpriteChecksum = -1;
                    var msg = $"Graphics cache file checksum mismatch: {path}. {checksum} != {atlas.AtlasSpriteChecksum}";
                    D.Err(msg);
                    CrashWatchdog.UpdateBoot(msg);
                    return false;
                }
            }
            return true;
        }

        // 4.2 sec
        private IEnumerator LoadCache(GraphicsCacheData cache, Action<GraphicsCacheData, bool> onDone) {
            var start = Time.realtimeSinceStartupAsDouble;
            foreach (var atlas in cache.Atlases) {
                if (!LoadCachedAtlas(atlas)) {
                    onDone(cache, false);
                    yield break;
                }
                if (FrameTimer.HasToYieldSlow()) {
                    yield return null;
                }
            }
            var end = Time.realtimeSinceStartupAsDouble;
            D.Warn("Loading atlas cache (direct) took: {0:0.000}s", end - start);
            onDone(cache, true);
        }

        private IEnumerator LoadCacheParallel(GraphicsCacheData cache, Action<GraphicsCacheData, bool> onDone) {
            var start = Time.realtimeSinceStartupAsDouble;
            var total = cache.Atlases.Count;
            var loaded = 0;
            var failed = new HashSet<string>();
            The.SysSig.AddLoadingStages.Send(total);
            yield return CachedAtlasLoader.Load(cache.Atlases, GraphicsCacheDir,
                onLoaded: (atlas, tex) => {
                    loadedCachedAtlases[atlas.Id] = tex;
                    loaded++;
                    LoadingMsg($"Loading graphics cache (Quality: {spriteQualityText}) [{loaded}/{total}] VRAM: {The.Alloc.AllocatedGPUMB}");
                    AdvanceStage();
                },
                onFailed: atlas => failed.Add(atlas.Info.Id));
            // The incremental path rebuilds an atlas whole: it keeps the pages already loaded
            // and drops every page of an atlas that lost one
            foreach (var atlas in cache.Atlases) {
                if (failed.Contains(atlas.Info.Id)) {
                    atlas.SpriteChecksum = -1;
                }
            }
            AdvanceStage();
            yield return null;
            var end = Time.realtimeSinceStartupAsDouble;
            #if PRODUCTION_BUILD
            D.Warn("Loading atlas cache took: {0:0.000}s", end - start);
            #endif

            onDone(cache, failed.Count == 0);
        }

        // Removes every page of these atlases from the cache, with its file and texture, so
        // the atlas is rebuilt whole. The rebuild replaces the sprites of a disposed texture.
        private void DropCachedAtlases(GraphicsCacheData cache, HashSet<string> atlasIds) {
            if (atlasIds.Count == 0) { return; }
            for (int i = cache.Atlases.Count - 1; i >= 0; i--) {
                var atlas = cache.Atlases[i];
                if (!atlasIds.Contains(atlas.Info.Id)) { continue; }
                cache.Atlases.RemoveAt(i);
                var file = AtlasBuilder.AtlasPath(GraphicsCacheDir, atlas.Id);
                if (File.Exists(file) && !Files.Delete(file)) {
                    D.Err("Failed to delete cached file: {0}", file);
                }
                if (loadedCachedAtlases.TryGetValue(atlas.Id, out var tex)) {
                    loadedCachedAtlases.Remove(atlas.Id);
                    The.Alloc.UnregisterAndDispose(tex);
                }
            }
        }

        private bool LoadCachedAtlas(CachedAtlas atlas) {
            var path = AtlasBuilder.AtlasPath(GraphicsCacheDir, atlas.Id);
            try {
                var startTime = Time.realtimeSinceStartupAsDouble;
                // atlas is already rescaled when we load from cache
                if (!AtlasBuilder.LoadCachedAtlas(path, atlas)) {
                    return false;
                }
                LoadingMsg($"Loading graphics cache (Quality: {spriteQualityText}) VRAM: {The.Alloc.AllocatedGPUMB}");
                var endTime = Time.realtimeSinceStartupAsDouble;
                #if LOG_GFX_TIMING
                D.Warn("Loading cached atlas {0} took {1:0.00}s",
                    atlas.Id, endTime - startTime);
                #endif
                return true;
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed loading cached atlas: {0}", path);
                return false;
            }
        }

        private void PreloadCustomTexture(string id, string gPath) {
            var path = Files.CombinePath(gPath, id);
            var aInfo = new AtlasInfo {
                Id = "Custom",
                SkipRescale = true,
            };
            RenderingService.Sprites.PreloadToAtlas(aInfo, id, path);
            AddSpriteChecksum(id, path, aInfo);
        }

        private static void AddSpriteChecksum(string id, string path, AtlasInfo aInfo) {
            unchecked {
                var sum = Sprites.Checksum(id, path, aInfo.Id);
                spriteChecksum = Hashes.Quick(spriteChecksum, sum);
                // D.Log("Adding sprite checksum. Id: {0}, Path: {1} ({2}), Ainfo: {3}",
                //     id, path, size, aInfo);
                if (!atlasMetadata.TryGetValue(aInfo.Id, out var meta)) {
                    meta.checksum = Hashes.Quick(Primes.Prime003, aInfo.Checksum);
                    meta.minNumAtlases = aInfo.MinimizeNumAtlases;
                    // Include atlas config (MipMapBias, TexturePadding, etc.) in the
                    // global checksum so cache rebuilds when atlas settings change,
                    // not only when sprite files change.
                    spriteChecksum = Hashes.Quick(spriteChecksum, aInfo.Checksum);
                }
                meta.checksum = Hashes.Quick(meta.checksum, sum);
                atlasMetadata[aInfo.Id] = meta;
            }
        }

        private void PreloadOneTexture(string id, string gPath, ModInfo info) {
            var path = Files.CombinePath(gPath, id);
            AtlasInfo aInfo;
            // search for atlas in core mod first
            bool foundAtlas = FindAtlasFor(id, coreMod, out aInfo);
            if (!foundAtlas) {
                if (info == coreMod) {
                    throw new Exception(string.Format(
                        "Atlas not defined for texture in core mod: {0} -> {1}",
                            id, gPath));
                }
                // try to find the atlas for this sprite in core mod
                foundAtlas = FindAtlasFor(id, info, out aInfo);
                if (!foundAtlas) {
                    // this texture is not an override and doesn't match any
                    // core atlases, it can go to Default atlas then
                    aInfo = new AtlasInfo {
                        Id = "Default",
                    };
                }
            }
            if (aInfo.SkipAtlas) {
                RenderingService.Sprites.PreloadRaw(aInfo, id, path);
            } else {
                RenderingService.Sprites.PreloadToAtlas(aInfo, id, path);
                AddSpriteChecksum(id, path, aInfo);
            }
        }

        private static bool FindAtlasFor(string id, ModInfo info, out AtlasInfo aInfo) {
            if (Arrays.IsEmpty(info.Atlases)) {
                aInfo = default;
                return false;
            }
            var normalizedId = Files.ToForwardSlashes(id);
            for (int i = 0; i < info.Atlases.Length; i++) {
                AtlasInfo atlas = info.Atlases[i];
                if (normalizedId.StartsWith(atlas.Path, StringComparison.Ordinal)) {
                    aInfo = atlas;
                    return true;
                }
                if (!Arrays.IsEmpty(atlas.ExtraPaths)) {
                    foreach (var ep in atlas.ExtraPaths) {
                        if (string.IsNullOrEmpty(ep)) { continue; }
                        if (normalizedId.StartsWith(ep, StringComparison.Ordinal)) {
                            aInfo = atlas;
                            return true;
                        }
                    }
                }
            }
            aInfo = default;
            return false;
        }

        // Sprites are added in the same order as before, so the atlases pack the same
        private IEnumerator PrepareAtlasesForPacking(List<PreloadedAtlas> atlases) {
            var sprites = new List<(PreloadedAtlas atlas, string id, string path)>();
            foreach (var pAtlas in atlases) {
                foreach (var spr in pAtlas.Sprites) {
                    sprites.Add((pAtlas, spr.Key, spr.Value));
                }
            }
            if (sprites.Count == 0) { yield break; }
            var paths = sprites.Select(s => s.path).ToList();
            var workers = Mathf.Clamp(SystemInfo.processorCount - 1, 1, 8);
            using (var queue = new SpriteDecodeQueue(paths, workers)) {
                for (int i = 0; i < sprites.Count; i++) {
                    var (pAtlas, id, path) = sprites[i];
                    if (FrameTimer.HasToYieldSlow()) {
                        LoadingMsg($"Loading textures: {pAtlas.Atlas.Id} [{i + 1}/{sprites.Count}]. VRAM: {The.Alloc.AllocatedGPUMB}");
                        yield return null;
                    }
                    SpriteDecodeQueue.Sprite decoded;
                    if (!queue.TryTake(i, out decoded)) {
                        var waitSpan = BootTimeline.Span("Sprite decode wait");
                        while (!queue.TryTake(i, out decoded)) {
                            yield return null;
                        }
                        waitSpan.Dispose();
                    }
                    var skipRescale = pAtlas.Atlas.SkipRescale;
                    if (decoded.IsDecoded && (skipRescale || !Res.RescalesSprites)) {
                        // No Texture2D: the atlas page is built from the pixels on a worker
                        RenderingService.Sprites.AddToAtlas(pAtlas.Atlas, id, decoded.Width,
                            decoded.Height, decoded.Rgba);
                        continue;
                    }
                    Texture2D tex;
                    if (decoded.IsDecoded) {
                        tex = Res.SpriteTexture(path, decoded.Width, decoded.Height,
                            decoded.Rgba, skipRescale);
                        decoded.Rgba.Dispose();
                    } else {
                        tex = Res.LoadTexture(path, mips: false, makeNonReadable: false,
                            skipRescale: skipRescale);
                    }
                    RenderingService.Sprites.AddToAtlas(pAtlas.Atlas, id, tex);
                }
                BootTimeline.Record("Sprite decode (workers)", queue.WorkSec);
            }
        }
        public void ReloadTunables() {
            Tunable.Reset();
            foreach (var mod in loadedMods) {
                var modInfoPath = ModFile(mod.Id, "ModInfo.json");
                var info = DeserializeModFile<ModInfo>(modInfoPath);
                var tunablePath = ModFolder(mod.Id, "Config/Tunable");
                LoadTunables(mod.Id, tunablePath, info);
            }
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Not reliable, used for development only
        /// </summary>
        public void ReloadDefs() {
            Ready.SetCore(false, "Reloading definitions");

            The.Defs.Clear();

            // FIXME copy paste from Load()
            //discovery.CollectMods();

            //var modDirs = Res.ListStreamingDirs(ModFolder);
            var modDirs = discovery.Available;
            ModInfos = new();
            loadedMods.Clear();
            orderedLoadedMods = null;
            availableMods.Clear();

            foreach (var modDir in modDirs) {
                //var modInfoPath = ModFilePath(mod, "ModInfo.json");
                var modInfoPath = $"{modDir}/ModInfo.json";
                if (!File.Exists(modInfoPath)) {
                    D.Err("Skipping mod folder without ModInfo.json: {0}", modDir);
                    WPopup.Create(true, null, "SKIPPING MOD", modDir,
                        $"{modInfoPath} does not exist").Spawn(UIPoolRoot.SystemPopupContainer);
                    continue;
                }
                try {
                    var info = DeserializeModFile<ModInfo>(modInfoPath);
                    if (info == null) { continue; }
                    DecorateModInfo(info, modDir);

                    if (availableMods.Contains(info.Id)) {
                        var msg = string.Format("Mod with ID {0} is already loaded from {1}. Skipping {2}",
                            info.Id, ModInfos[info.Id].FullPath, modDir);
                        D.Err(msg);
                        WPopup.Create(true, null, "SKIPPING DUPLICATE MOD", info.Id, msg).Spawn(UIPoolRoot.SystemPopupContainer);
                        continue;
                    }
                    ModInfos[info.Id] = info;
                    availableMods.Add(info.Id);
                    if (info.Id == "Core") {
                        info.IsEnabled = true;
                    } else {
                        var def = info.Id == "Translations";
                        info.IsEnabled = The.Prefs.GetBoolAutoHash(
                            ModEnabledKey(info.Id), def);
                    }
                } catch (Exception ex) {
                    D.LogEx(ex, $"ModInfo parsing failed for {modInfoPath}");
                    WPopup.Create(true, null, "SKIPPING MOD", null,
                        $"Failed to parse JSON file: {modInfoPath}").Spawn(UIPoolRoot.SystemPopupContainer);
                }
            }

            // 1. load mods that have no dependencies
            foreach (var mod in ModInfos) {
                if (!mod.Value.IsEnabled) { continue; }
                if (mod.Value.IsDevDisabled) { continue; }
                if (mod.Value.IsRuntimeBroken) { continue; }
                if (Arrays.IsEmpty(mod.Value.Dependencies)) {
                    LoadDefs(mod.Value);
                    if (!mod.Value.IsRuntimeBroken) {
                        loadedMods.Add(mod.Value);
                    }
                }
            }

            List<string> loadedModIds = new();

            int attempts = 10;
            while (loadedMods.Count < ModInfos.Count && attempts-- > 0) {
                // Try to incrementally load all the mods that we need in order
                foreach (var mod in ModInfos) {
                    if (!mod.Value.IsEnabled) { continue; }
                    if (mod.Value.IsDevDisabled) { continue; }
                    if (mod.Value.IsRuntimeBroken) { continue; }
                    if (Arrays.IsEmpty(mod.Value.Dependencies)) { continue; }
                    if (loadedMods.Contains(mod.Value)) { continue; }
                    // the continue inside the loop only skipped to the next dependency
                    var hasAllDeps = true;
                    foreach (var dep in mod.Value.Dependencies) {
                        if (!IsModLoaded(dep)) {
                            hasAllDeps = false;
                            break;
                        }
                    }
                    if (!hasAllDeps) { continue; }
                    // All dependencies are loaded! OK to load
                    LoadDefs(mod.Value);
                    if (!mod.Value.IsRuntimeBroken) {
                        loadedMods.Add(mod.Value);
                        var modId = mod.Value.Id;
                        if (modId != "Core" && modId != "Translations") {
                            loadedModIds.Add(modId);
                        }
                    }
                }
            }

            orderedLoadedMods = loadedMods.Select(m => m.Id).ToArray();
            The.Defs.AfterLoad();

            Ready.SetCore(true, "Done reloading definitions");
            var s = The.R.State;
            ResearchTree.Cleanup();
            s.Research.Clear();
            s.Research.InitializeDatoids();
            The.Defs.ResolveUnlocks();
            s.Research.Balance();
            s.Research.ValidateTree();
        }
        #endif

        #if UNITY_EDITOR
        // Used for dev tooling
        public static List<Def> QuickLoadCoreDefs(string subdir) {
            var defsPath = CoreModFilePath(subdir);
            var defs = Res.ListFilesRecursive(defsPath, ".json");
            var defsResult = new List<Def>();

            // Root definitions
            foreach (var defName in defs) {
                // won't be patched here since it's only for core mod
                var json = Files.ReadJSONWithComments(Files.CombinePath(defsPath, defName));
                var def = Json.Deserialize<Def>(json, defName);
                if (string.IsNullOrEmpty(def.Id)) {
                    def.Id = Files.ToPathId(defName);
                }
                if (def.Id.StartsWith("Templates/")) {
                    def.IsTemplate = true;
                }
                defsResult.Add(def);
            }
            return defsResult;
        }
        public static List<T> QuickLoadCoreJson<T>(string subdir) {
            var defsPath = CoreModFilePath(subdir);
            var defs = Res.ListFilesRecursive(defsPath, ".json");
            var defsResult = new List<T>();

            // Root definitions
            foreach (var defName in defs) {
                // won't be patched here since it's only for core mod
                var json = Files.ReadJSONWithComments(Files.CombinePath(defsPath, defName));
                var def = Json.Deserialize<T>(json, defName);
                var idField = def.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public);
                if (idField != null && string.IsNullOrEmpty((string) idField.GetValue(def))) {
                    idField.SetValue(def, Files.ToPathId(defName));
                }
                defsResult.Add(def);
            }
            return defsResult;
        }
        #endif
        // Used for dev tooling
        public static string[] QuickLoadCoreGraphics() {
            var gfxPath = CoreModFilePath("Graphics");
            var gfx = Res.ListFilesRecursive(gfxPath, ".png");
            return gfx;
        }

        private Def LoadOneDef(string defsPath, string defName,
                List<Def> defsWithParents, ModInfo info) {
            var jsonPath = Files.CombinePath(defsPath, defName);
            var json = LoadPatchedJson(jsonPath);
            if (!Json.TryDeserialize<Def>(json, defName, out var def,
                    out var error)) {
                ShowFileError(jsonPath, error);
                info.MarkRuntimeBroken($"Failed parsing def json: {defName}");
                return null;
            }

            // For backwards compatibility with unity's shitty json deserializer
            // def.Researchable ??= new Researchable();

            if (string.IsNullOrEmpty(def.Id)) {
                def.Id = Files.ToPathId(defName);
            }
            if (def.IsAbstract) {
                // Layer is optional for abstract
                def.LayerId = -1;
            } else {
                def.LayerId = WorldLayer.ToId(def.Layer);
                if (def.LayerId == -1) {
                    D.Err("Layer is not defined in JSON: {0}.json. This definition will not be loaded!", def.Id);
                }
            }
            The.SysSig.AfterDefInitialized.Send(def);

            // if there is a parent def, let's process this later
            if (!string.IsNullOrWhiteSpace(def.ParentId)) {
                defsWithParents.Add(def);
                return null;
            }

            return def;
        }
        private bool HasMissingPrerequisites(Def def, out string firstMissingId) {
            firstMissingId = null;
            if (def.Researchable.Prerequisites == null) {
                D.Err("Researchable has null prerequisites: {0}", def.Id);
                return false;
            }
            for (int i = def.Researchable.Prerequisites.Length - 1; i >= 0; i--) {
                string prereq = def.Researchable.Prerequisites[i];
                if (!The.Defs.Defs.ContainsKey(prereq)) {
                    firstMissingId = prereq;
                    return true;
                }
            }
            return false;
        }

        public bool ValidateResearchablePostLoad(Def def) {
            if (def.Researchable == null) { return true; }

            if (!Researchable.TreeNode(def)) { return true; }
            if (def.Tier == 0) { return true; }
            if (HasMissingPrerequisites(def, out var missingId)) {
                if (ModInfos.TryGetValue(def.ModId, out var info)) {
                    ModRegistry.MarkRuntimeBroken(info, $"Research node has missing prerequisite {def}: {missingId}");
                    ModRegistry.ShowModErrorPopup(info,
                        $"Misconfigured research node: <b>{def.Id}</b>. Prerequisite def is not found by id: <b>{missingId}</b>");
                } else {
                    D.Err("Couldn't get mod info for def with missing research prerequisites: {0}: {1}",
                        def, missingId);

                }
                return false;
            }
            return true;
        }

        private bool ValidateResearchable(Def def, ModInfo info) {
            if (def.Researchable == null) { return true; }
            if (!Researchable.TreeNode(def)) { return true; }
            if (def.Tier == 0) { return true; }
            if (Arrays.IsEmpty(def.Researchable.DatoidCosts)) {
                defErrors[def] = $"Misconfigured research node. <b>DatoidCosts</b> are not defined.";
                return false;
            }
            return true;
        }

        private bool ValidateComponentsFor(Def def, ModInfo info) {
            if (def.Components == null) { return true; }
            bool isValid = true;
            var processed = Caches.IntSet;
            processed.Clear();
            var errSB = Caches.StringBuilder2;
            errSB.Clear();
            foreach (var comp in def.Components) {
                if (comp.Component == null) {
                    #if PRODUCTION_BUILD
                    errSB.AppendFormat("Invalid component for def: {0}. Component is null in json!\n", def.Id);
                    isValid = false;
                    #else
                    D.Err("Invalid component for def: {0}. Component is null in json!", def.Id);
                    #endif
                    continue;
                }
                if (!processed.Add(comp.ComponentH)) {
                    errSB.AppendFormat(
                        "Duplicate component detected! <b>{0}<b>\n",
                            comp.Component);
                    isValid = false;
                }
                if (!The.Defs.HasComponentPrototype(comp, out var error)) {
                    errSB.AppendFormat(
                        "Trying to load a component that does not exist: <b>{0}<b>: {1}\n",
                            comp.Component, error);
                    isValid = false;
                }
            }
            if (!isValid) {
                defErrors[def] = errSB.ToString();
            }
            return isValid;
        }

        // ReloadDefs reloads in one go
        private void LoadDefs(ModInfo info) {
            var run = LoadDefsAsync(info);
            while (run.MoveNext()) { }
        }

        // Yields when a frame's budget is spent: Core has hundreds of defs, and reading them
        // all in one frame froze the loading screen for seconds on slow disks
        private IEnumerator LoadDefsAsync(ModInfo info) {
            defErrors.Clear();
            var start = Time.realtimeSinceStartupAsDouble;
            var defsPath = ModFolder(info.Id, "Definitions");
            if (!Directory.Exists(defsPath)) { yield break; }
            var defs = Res.ListFilesRecursive(defsPath, ".json");
            var defsWithParents = new List<Def>();

            // Root definitions
            foreach (var defName in defs) {
                var def = LoadOneDef(defsPath, defName, defsWithParents, info);
                if (def != null) {
                    D.Ass(def.IsAbstract || !string.IsNullOrEmpty(def.Layer),
                        "Layer field not set for def: {0}", defName);
                    #if DEBUG_MODS
                    D.Log("Loading {0}", defName);
                    #endif
                    TrySetDef(info, def);
                }
                if (FrameTimer.HasToYieldSlow()) {
                    yield return null;
                }
            }

            var missing = new List<Def>(defsWithParents);
            int depth = 5;
            while (missing.Count > 0 && depth-- > 0) {
                try {
                    ProcessDefsWithParents(info, new List<Def>(missing), missing);
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed processing def with parents: {0}", info);
                    info.MarkRuntimeBroken($"Got exception when processing defs with missing parents: {ex.Message}");
                }
            }
            if (missing.Count > 0) {
                D.Err("Definitions could not be loaded, most likely missing ParentId: {0}",
                    string.Join(",", missing));
                info.MarkRuntimeBroken($"Failed processing missing defs with parents: {string.Join(", ", missing)}");
            }
            #if DEBUG_MODS
            var end = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Mod loading defs {0} took {1:0.00}s", info.Id, end - start);
            #endif
            if (defErrors.Count > 0) {
                info.MarkRuntimeBroken($"Def errors: {defErrors.Count}");
                var sb = Caches.StringBuilder2;
                sb.Clear();
                int count = 0;
                sb.AppendFormat("Def errors: <b>{0}</b>", defErrors.Count);
                sb.Append(GlyphMap.BRHR);
                foreach (var de in defErrors) {
                    sb.AppendFormat("<size=85%><b>{0}</b></size> <b>{1}</b> <size=85%>{2}</size>\n",
                        de.Key.Id,
                        GlyphMap.ArrowRight,
                        de.Value);
                    D.Err("{0}: {1}", de.Key.Id, de.Value);
                    if (count++ > 5) {
                        break;
                    }
                }
                var msg = sb.ToString();
                ModRegistry.ShowModErrorPopup(info, msg);
            }
        }

        private void ProcessDefsWithParents(ModInfo info, List<Def> defs,
                List<Def> missing) {
            missing.Clear();
            // Complete loading of defs that have parents
            for (int i = 0; i < defs.Count; i++) {
                Def def = defs[i];
                if (!The.Defs.Contains(def.ParentId)) {
                    missing.Add(def);
                    continue;
                }
                var parent = The.Defs.Get(def.ParentId);
                D.Ass(parent != null, "Could not find parent for def: {0}: {1}",
                    def, def.ParentId);

                var finalDef = parent.Copy();
                finalDef.Id = def.Id;
                if (!string.IsNullOrWhiteSpace(def.ToolPath)) {
                    finalDef.ToolPath = def.ToolPath;
                }
                // Not inherited, see Def.BuildGroup
                finalDef.BuildGroup = def.BuildGroup;
                finalDef.IsUnfinished |= def.IsUnfinished;
                finalDef.ParentId = def.ParentId;
                finalDef.IgnoreMarket |= def.IgnoreMarket;
                if (def.Seasonal != null) {
                    finalDef.Seasonal = def.Seasonal;
                }
                // This only does it for research nodes!
                finalDef.Tier = Mathf.Max(finalDef.Tier, def.Tier);
                finalDef.ResearchValue = def.ResearchValue > 0
                    ? def.ResearchValue : finalDef.ResearchValue;
                // 1 is both the default and "unset", so the child only wins when it says
                // something other than 1 - see the field comment on Def.WealthValueMul.
                finalDef.WealthValueMul = def.WealthValueMul != 1f
                    ? def.WealthValueMul : finalDef.WealthValueMul;
                finalDef.IsImportant |= def.IsImportant;
                finalDef.IsAbstract = finalDef.IsAbstract && def.IsAbstract;
                finalDef.Researchable = Researchable.Merge(
                    finalDef.Researchable, def.Researchable);
                finalDef.Spawnable = GenSpawnable.Merge(finalDef.Spawnable,
                    def.Spawnable);
                finalDef.Layer = def.Layer;
                finalDef.LayerId = def.LayerId;
                finalDef.TranslationVars = def.TranslationVars;
                finalDef.UseParentTexts = def.UseParentTexts;
                finalDef.NameKey = def.NameKey;
                finalDef.DescKey = def.DescKey;
                if (!string.IsNullOrWhiteSpace(def.PreviewIcon)) {
                    finalDef.PreviewIcon = def.PreviewIcon;
                }
                finalDef.SkipToolListing |= def.SkipToolListing;

                // merge components
                var newComponents = new List<ComponentConfig>();
                if (def.Components != null) {
                    foreach (var comp in def.Components) {
                        if (comp.Component == null) {
                            D.Err("Misconfigured component found in def: {0}. Component name is null!",
                                def.Id);
                            continue;
                        }
                        var cc = finalDef.ComponentConfigFor(comp.ComponentH, false);
                        if (cc == null) {
                            newComponents.Add(comp);
                            // add this component
                        } else {
                            //// D.Log("Merging config of {0} {1} with {2}", def.Id, cc.Component, comp.Component);
                            cc.MergeWith(comp);
                        }
                    }
                }

                // add new components that didn't exist before
                var newCompSize = newComponents.Count;
                if (newCompSize > 0) {
                    // a parent without components copies as null: this threw, and the rest
                    // of the mod's defs in the batch were dropped without a report
                    var origSize = finalDef.Components?.Length ?? 0;
                    Array.Resize(ref finalDef.Components, origSize + newCompSize);
                    foreach (var newComp in newComponents) {
                        finalDef.Components[origSize++] = newComp;
                    }
                }

                TrySetDef(info, finalDef);
            }
        }

        private bool TrySetDef(ModInfo info, Def def) {
            def.ModId = info.Id;
            def.IsCoreMod = info == coreMod;
             if (!ValidateComponentsFor(def, info)) {
                return false;
            }

            if (!ValidateResearchable(def, info)) {
                return false;
            }
            // We are ready to register our abomination
            if (!def.IsAbstract && string.IsNullOrWhiteSpace(def.Layer)) {
                defErrors[def] = "Layer is not defined in JSON!";
                return false;
            }

            if (!The.Defs.Set(def)) {
                defErrors[def] = "Mod overriding core def in full is not allowed. Use JSON Patching instead!";
                return false;
            }
            return true;
        }

        public static string CoreModFilePath(string path) {
           return $"{Application.streamingAssetsPath}/{ModFolderName}/Core/{path}";
        }

        public string TryFile(string path) {
            for (int i = orderedLoadedMods.Length - 1; i >= 0; i--) {
                string mod = orderedLoadedMods[i];
                var f = ModFile(mod, path);
                // D.Warn("Trying mod file: {0} | {1}", mod, path);
                if (File.Exists(f)) {
                    return f;
                }
            }
            return null;
        }
        public string ModFile(string mod, string path) {
            return ModInfos[mod].ModFile(path);
        }
        public string ModFile(string mod, string folder, string file) {
            return ModInfos[mod].ModFile(folder, file);
        }
        public string ModFolder(string mod, string folder) {
            return ModInfos[mod].ModFolder(folder);
        }
        public string ModFolder(string mod, string folder, string folder2) {
            return ModInfos[mod].ModFolder(folder, folder2);
        }

        // Workshop items are known by their folder, not the file in it
        private bool TryDeserializeModInfo(string modDir, string path, out ModInfo info,
                out string error) {
            bool strict = !discovery.IsWorkshop(modDir);
            var json = LoadPatchedJson(path);
            if (!Json.TryDeserialize(json, path, out info, out error, strict)) {
                return false;
            }
            if (info == null) {
                error = "The file is empty";
                return false;
            }
            return true;
        }

        private T DeserializeModFile<T>(string path) where T : new() {
            bool strict = !discovery.IsWorkshop(path);
            var json = LoadPatchedJson(path);
            return Json.Deserialize<T>(json, path, strict: strict);
        }

        public string ModButtonText() {
            var stat = Units.XOutOfY(loadedMods.Count, availableMods.Count);
            stat = Texts.GreySmaller(stat);
            return $"{T.MenuMods}\n{stat}";
        }
        public string ShipsButtonText() {
            var cnt = Units.Num(discovery.SharedSaves.Count);
            var stat = Texts.GreySmaller(cnt);
            return $"{T.NounShips}\n{stat}";
        }

        public string ModButtonTooltip() {
            var list = Caches.StringList;
            list.Clear();
            foreach (var modId in orderedLoadedMods) {
                var mod = ModInfos[modId];
                list.Add(ModName(mod));
            }
            return string.Join("\n", list);
        }

        public string ModTagFor(Def def) {
            if (def.IsCoreMod) { return null; }
            var modIcon = GlyphMap.Sprite("IconsWhite", "Repair");
            if (ModInfos.TryGetValue(def.ModId, out var mod)) {
                var modV = mod.ModVersion;
                return $"{modIcon}{Texts.Accented(mod.Name)} ({modV})";
            }
            return $"{modIcon}{Texts.Red(T.Unknown)}";
        }
        public static string ModName(string modId) {
            if (The.ModLoader.ModInfos.TryGetValue(modId, out var mod)) {
                var modV = mod.ModVersion;
                return $"<b>{mod.Name}</b> ({modV})";
            }
            return Texts.Red(modId);
        }
        public static string ModName(ModInfo mod) {
            if (mod == null) {
                return T.Unknown;
            }
            var modV = mod.ModVersion;
            return $"<b>{mod.Name}</b> ({modV})";
        }
        public static string ModName(ModInfo mod, string textColor) {
            var modV = mod.ModVersion;
            return $"<b>{mod.Name}</b> ({Texts.WithColor(modV, textColor)})";
        }


        public bool ValidateMods(GameStateMeta meta,
                out string backupFile,
                out int missingModsCnt, out
                string missingModsList) {
            var mods = meta.Mods;
            missingModsCnt = 0;
            if (mods == null) {
                backupFile = null;
                missingModsList = null;
                return true;
            }
            Caches.StringBuilder2.Clear();
            foreach (var mod in mods) {
                if (!IsModLoaded(mod.Key)) {
                    Caches.StringBuilder2.AppendFormat("\n - <b>{0}</b> ({1})",
                        mod.Key, mod.Value);
                    missingModsCnt++;
                }
            }
            if (missingModsCnt > 0) {
                missingModsList = Caches.StringBuilder2.ToString();
                backupFile = SaveLoadUtils.CreateBackupFor(meta);
                return false;
            } else {
                backupFile = null;
                missingModsList = null;
                return true;
            }
        }

        private static readonly StringBuilder selfInitLog = new StringBuilder();
        public static void Init(SelfInit.Stage stage) {
            var result = new SelfInit.StageResult();
            result.Success = SelfInit.InitStage(stage, selfInitLog,
                out result.ScanSec, out result.InvokeSec);
            LogStage(stage, result);
        }

        // Like Init, but a stage that takes longer than a frame's budget continues on the next
        // frame instead of freezing the loading screen
        public static IEnumerator InitAsync(SelfInit.Stage stage) {
            var result = new SelfInit.StageResult();
            yield return SelfInit.InitStageAsync(stage, selfInitLog, result,
                FrameTimer.HasToYieldSlow);
            LogStage(stage, result);
        }

        private static void LogStage(SelfInit.Stage stage, SelfInit.StageResult result) {
            BootTimeline.Record($"SelfInit {stage} scan", result.ScanSec);
            BootTimeline.Record($"SelfInit {stage}", result.InvokeSec);
            D.Warn("Self init ({0}) took: {1:0.000}s (scan {2:0.000}s)",
                stage, result.ScanSec + result.InvokeSec, result.ScanSec);
            if (selfInitLog.Length > 0) {
                if (!result.Success) {
                    D.Err(selfInitLog.ToString());
                } else {
                    D.Warn(selfInitLog.ToString());
                }
                selfInitLog.Clear();
            }
        }
    }
}
