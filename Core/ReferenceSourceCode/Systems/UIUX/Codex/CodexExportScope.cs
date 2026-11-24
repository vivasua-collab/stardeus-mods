using System;
using System.Collections.Generic;
using Game.Input;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public enum CodexExportBlockType { Text, Image, Video }

    public struct CodexExportBlock {
        public CodexExportBlockType Type;
        public string Payload;
    }

    // Active for the full duration of an editor-side codex site export (see
    // Docs/Architecture/Plans/CodexWikiExport.md and CodexSiteExporter.Export). While a
    // scope exists, CodexConceptPage block helpers capture their content here instead of
    // spawning widgets, binding glyph text resolves through the injected
    // machine-independent resolver, and Def.IsInSeason treats every def as in-season so
    // the exported site is a timeless reference independent of the export's real-world
    // run date (see Docs/Architecture/SeasonalContent.md). Inert during normal play.
    public sealed class CodexExportScope : IDisposable {
        private static CodexExportScope current;
        public static CodexExportScope Current => current;
        public static bool IsActive => current != null;

        private readonly List<CodexExportBlock> blocks = new();
        private readonly Func<ActionType, string> glyphResolver;
        private int baseIdx;

        public CodexExportScope(Func<ActionType, string> glyphResolver) {
            this.glyphResolver = glyphResolver;
            current = this;
        }

        [SelfInit]
        private static void Init() {
            current = null;
        }

        public static string ResolveGlyph(ActionType act) {
            return current?.glyphResolver?.Invoke(act);
        }

        public void BeginPage(int firstSiblingIdx) {
            baseIdx = firstSiblingIdx;
            blocks.Clear();
        }

        public void Add(CodexExportBlockType type, string payload, int siblingIdx) {
            // Mirrors Unity sibling-index insertion: spawning at an already-used
            // index pushes previously inserted blocks down, so pages that reuse
            // an index export in the same order they render in-game.
            var at = Mathf.Clamp(siblingIdx - baseIdx, 0, blocks.Count);
            blocks.Insert(at, new CodexExportBlock { Type = type, Payload = payload });
        }

        public void CopyBlocksTo(List<CodexExportBlock> into) {
            into.AddRange(blocks);
        }

        public void Dispose() {
            if (current == this) {
                current = null;
            }
        }
    }
}
