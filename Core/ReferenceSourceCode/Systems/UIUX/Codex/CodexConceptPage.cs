using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Base class for a single Codex "Operations Manual" page. Subclasses are
    /// discovered automatically (see <see cref="LoadKnown"/>) - just add a new
    /// subclass anywhere in the project (or a mod assembly) and it self-registers,
    /// no manual wiring needed.
    /// </summary>
    public abstract class CodexConceptPage {
        private struct CodexPageSorter : IComparer<CodexConceptPage> {
            public int Compare(CodexConceptPage x, CodexConceptPage y) {
                var c = x.Order.CompareTo(y.Order);
                return c != 0 ? c : string.Compare(x.TitleT, y.TitleT);
            }
        }

        public static void GetGlobalCounts(out int total, out int learned) {
            total = All.Count;
            learned = All.Count(i => i.IsLearned);
        }

        public static readonly List<CodexConceptPage> All = new();
        public static readonly List<CodexConceptPage> Groups = new();
        public static readonly Dictionary<string, List<CodexConceptPage>> ByGroup = new();
        private static readonly Dictionary<string, CodexConceptPage> byId = new();
        private static readonly IComparer<CodexConceptPage> PageSorter = new CodexPageSorter();

        // Mirrors SelfInit's assembly blacklist (Assets/Code/Game.Utils/SelfInit.cs) -
        // we're doing our own bit of reflection here, so skip the same noise.
        private static readonly string[] assemblyBlacklist = {
            "Unity", "mscorlib", "netstandard", "KLUnityUtils", "ProjectDawn.",
            "MessagePack", "ThirdParty", "Uween", "Microsoft.", "Bee.", "0Harmony",
            "AutomaticGraphLayout", "ExCSS", "System", "websocket", "Newtonsoft",
            "Mono.", "com.rlabrecque.",
        };

        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            Groups.Clear();
            ByGroup.Clear();
            byId.Clear();
            All.Clear();
        }

        public static CodexConceptPage FindGroup(string id) {
            LoadKnown();
            foreach (var g in Groups) {
                if (g.Id == id) {
                    return g;
                }
            }
            return null;
        }

        public static CodexConceptPage FindById(string id) {
            LoadKnown();
            return byId.Get(id, null);
        }

        public static void LoadKnown() {
            if (Groups.Count != 0) { return; }

            foreach (var pageType in FindPageTypes()) {
                CodexConceptPage page;
                try {
                    page = (CodexConceptPage) Activator.CreateInstance(pageType);
                    D.Warn("Codex page: {0}", page.Id);
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed instantiating codex page: {0}", pageType);
                    continue;
                }
                LoadOne(page);
            }
            Groups.Sort(PageSorter);
        }

        private static List<Type> FindPageTypes() {
            var result = new List<Type>();
            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies()) {
                var fn = ass.FullName;
                bool isBlacklisted = false;
                for (int i = assemblyBlacklist.Length - 1; i >= 0; i--) {
                    if (fn.StartsWith(assemblyBlacklist[i])) {
                        isBlacklisted = true;
                        break;
                    }
                }
                if (isBlacklisted) { continue; }
                try {
                    foreach (var type in ass.GetTypes()) {
                        if (type.IsAbstract || !typeof(CodexConceptPage).IsAssignableFrom(type)) {
                            continue;
                        }
                        if (type == typeof(CodexConceptGroup)) {
                            // Synthesized internally by AddToGroup, not a real page
                            continue;
                        }
                        result.Add(type);
                    }
                } catch (Exception ex) {
                    UnityEngine.Debug.LogException(ex);
                    UnityEngine.Debug.LogErrorFormat("Failed getting types from assembly: {0}", ass);
                }
            }
            return result;
        }

        private static void LoadOne(CodexConceptPage c) {
            try {
                var rawId = c.Id;
                c.SaveId = rawId.Replace("/", ".");
                c.GroupFromId = c.SaveId.Split('.')[0];

                AddToGroup(c, c.GroupFromId);
                if (!Arrays.IsEmpty(c.LinkedGroups)) {
                    foreach (var lg in c.LinkedGroups) {
                        AddToGroup(c, lg);
                    }
                }
                All.Add(c);
                byId[rawId] = c;
            } catch (Exception ex) {
                WPopup.SystemWarning("CODEX LOAD FAIL", $"Failed loading codex page: {c.GetType().Name}.\n\n{ex.Message}");
                D.LogEx(ex, "Failed loading codex page: {0}", c.GetType().Name);
            }
        }

        public void GetCounts(out int total, out int learned) {
            if (ByGroup.TryGetValue(GroupFromId, out var g)) {
                total = g.Count;
                learned = g.Count(i => i.IsLearned);
            } else {
                total = learned = 0;
            }
        }

        public static void LoadState(CodexSysData codex) {
            LoadKnown();
            var learned = codex?.LearnedConcepts;
            foreach (var c in All) {
                c.IsLearned.SetSilent(learned != null && learned.Contains(c.SaveId));
            }
        }

        public static CodexSysData SaveState() {
            if (All.Count == 0) { return null; }
            var codex = new CodexSysData();
            codex.LearnedConcepts = new();
            foreach (var c in All) {
                if (c.IsLearned) {
                    codex.LearnedConcepts.Add(c.SaveId);
                }
            }
            return codex;
        }

        private static void AddToGroup(CodexConceptPage c, string gid) {
            if (!ByGroup.TryGetValue(gid, out var glist)) {
                glist = new List<CodexConceptPage>();
                ByGroup.Add(gid, glist);
                Groups.Add(new CodexConceptGroup(gid, () => c.GroupTitleT));
            }
            glist.Add(c);
            glist.Sort(PageSorter);
        }

        /// <summary>Author-facing id, e.g. "ShipCombat/Flux". Must stay stable - it's
        /// derived into the save-file key (see <see cref="SaveId"/>).</summary>
        public abstract string Id { get; }

        /// <summary>Dotted form of <see cref="Id"/> (e.g. "ShipCombat.Flux"), used as the
        /// save-file key and to derive <see cref="GroupFromId"/>. Set once during
        /// <see cref="LoadOne"/>/registration.</summary>
        public string SaveId { get; private set; }
        public string GroupFromId { get; private set; }

        /// <summary>Only for use by subclasses that bypass normal registration
        /// (i.e. <see cref="CodexConceptGroup"/>) to set their own ids directly.</summary>
        protected void SetGroupIds(string saveId, string groupFromId) {
            SaveId = saveId;
            GroupFromId = groupFromId;
        }
        public virtual bool IsGroup => false;
        public virtual string[] LinkedGroups => null;

        /// <summary>Optional explicit ordering within a group (and for groups
        /// themselves). Defaults to 0 for every page, which keeps the current
        /// alphabetical-by-title sort.</summary>
        public virtual int Order => 0;
        protected abstract string GroupTitleT { get; }
        public abstract string TitleT { get; }
        public readonly BBool IsLearned = new();

        /// <summary>Builds the page content by spawning blocks (text, images, ...)
        /// into <paramref name="group"/>, in the order they should appear.</summary>
        public abstract void Build(GameState s, Lifetime life, WGroup group, int idx);

        /// <summary>Whether this page's body (not just its title, which is always
        /// matched separately) matches the given search query.</summary>
        public abstract bool OnSearchPageBody(string query);

        protected static WText AddTextBlock(Lifetime life, WGroup group, string text, int siblingIdx) {
            if (CodexExportScope.IsActive) {
                CodexExportScope.Current.Add(CodexExportBlockType.Text,
                    Texts.Beautify(text), siblingIdx);
                return null;
            }
            var w = UISpawn.Widget<WText>(life,
                UIPrefabId.WTabPanelItem_TextBlock, group.Container, siblingIdx);
            w.Load(Texts.Beautify(text));
            w.RebuildTextLayout();
            return w;
        }

        protected static void AddImageBlock(Lifetime life, WGroup group,
                string imageResourcePath, int siblingIdx, int maxHeight = 512) {
            if (CodexExportScope.IsActive) {
                CodexExportScope.Current.Add(CodexExportBlockType.Image,
                    imageResourcePath, siblingIdx);
                return;
            }
            var sprite = Images.SpriteWithResourceFallback(imageResourcePath);
            if (sprite == null) { return; }
            var imgW = UISpawn.Widget<WButtonImageAutoSize>(life,
                UIPrefabId.WTabPanelItem_ButtonImageBlock, group.Container, siblingIdx);
            imgW.Load(sprite, () => The.SysSig.ShowImageBlowout.Send(sprite), heightLimit: maxHeight);
            UIUtils.AddTip(life, imgW, T.ClickToEnlarge);
            Coroutines.OnNextFrame(group.RebuildLayout);
        }

        protected static void AddVideoBlock(Lifetime life, WGroup group,
                string videoResourcePath, int siblingIdx, int maxHeight = 512) {
            if (CodexExportScope.IsActive) {
                CodexExportScope.Current.Add(CodexExportBlockType.Video,
                    videoResourcePath, siblingIdx);
                return;
            }
            var clip = Res.LoadVideoClip(videoResourcePath);
            if (clip == null) { return; }
            var vidW = UISpawn.Widget<WVideoBlockAutoSize>(life,
                UIPrefabId.WTabPanelItem_VideoBlock, group.Container, siblingIdx);
            vidW.Load(clip, () => The.SysSig.ShowVideoBlowout.Send(clip), heightLimit: maxHeight);
            UIUtils.AddTip(life, vidW, T.ClickToEnlarge);
            Coroutines.OnNextFrame(group.RebuildLayout);
        }

        protected static void AddCustomBlock(Lifetime life, WGroup group,
                Action<Lifetime, RectTransform, int> spawn, int siblingIdx = -1) {
            spawn(life, group.Container, siblingIdx);
        }

        protected static string CodexLink(string label, string target) {
            return Texts.LinkToCodex(label, target);
        }
        protected static string CodexManualLink(string label, string target) {
            return Texts.LinkToCodexManual(label, target);
        }

        protected static string DefLink(string defId) {
            var def = The.Defs.TryGet(defId);
            if (def == null) {
                D.Err("Codex page def link not found: {0}", defId);
                return defId;
            }
            return def.CodexLink;
        }

        /// <summary>The overlay's own tooltip text plus its shortcut glyph, e.g. for
        /// substituting "Heat overlay [H]" into a sentence.</summary>
        protected static string OverlayTip(GameState s, string overlayType) {
            foreach (var sys in s.Sys.Active) {
                if (sys is IOverlayProvider opr && opr.Overlays != null) {
                    foreach (var ovr in opr.Overlays) {
                        if (ovr.Id == overlayType) {
                            if (CodexExportScope.IsActive) {
                                // Canonical shortcut from the export glyph resolver -
                                // ShortcutGlyphWithTip is a cached presentation string
                                // built from live bindings / attached hardware.
                                var glyph = CodexExportScope.ResolveGlyph(ovr.ShortcutAction);
                                return glyph == null
                                    ? ovr.TooltipText
                                    : $"{ovr.TooltipText}{glyph}";
                            }
                            return $"{ovr.TooltipText}{ovr.ShortcutGlyphWithTip}";
                        }
                    }
                }
            }
            D.Err("Codex page overlay tip not found: {0}", overlayType);
            return overlayType;
        }

        /// <summary>A clickable link that opens the given overlay.</summary>
        protected static string OverlayLink(string overlayId) {
            return Texts.LinkToOverlay(overlayId);
        }

        protected static string BindingGlyph(ActionType at) {
            return The.Bindings.GetControlNameWithTip(at);
        }

        protected static string TunablePercent(string tunableId) {
            return Texts.Accented(Units.Percentage(Tunable.Float(tunableId)));
        }

        protected static bool QueryMatches(string query, params string[] values) {
            if (query == null) { return false; }
            foreach (var value in values) {
                var searchValue = Strings.ToSearchString(value, allowNull: true);
                if (searchValue != null && searchValue.Contains(query)) {
                    return true;
                }
            }
            return false;
        }
    }
}
