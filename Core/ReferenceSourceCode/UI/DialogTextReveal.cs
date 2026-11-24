using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;

namespace Game.UI {
    // Typewriter reveal for a TMP_Text and its linked overflow chain.
    // Full text is laid out once and glyphs are faded in through vertex data,
    // so words never reflow and the linked overflow split stays stable.
    public sealed class DialogTextReveal {
        private const float StartDelay = 0.12f;
        private const float CharDelay = 0.0165f;
        private const float MinorPause = 0.09f;
        private const float MajorPause = 0.21f;
        private const float MaxDuration = 5.25f;
        private const float GlyphFade = 0.09f;
        private const float GlyphRise = 3f;
        private const float SoundInterval = 0.045f;
        // Underline and strikethrough runs are three quads each
        private const int DecorationVerts = 12;

        private sealed class Target {
            public TMP_Text Text;
            public Color32[][] Colors = Array.Empty<Color32[]>();
            public Vector3[][] Vertices = Array.Empty<Vector3[]>();
            public int[] VertexCounts = Array.Empty<int>();
            // Where the character quads end in each material, decorations start there
            public int[] CharVertexEnd = Array.Empty<int>();
            public int MaterialCount;
            public bool HasSnapshot;
        }

        private readonly Target[] targets;
        private readonly Action onComplete;
        private readonly List<float> revealTimes = new();
        private bool isRevealing;
        private bool isStarted;
        private bool isSkipping;
        private int revealCursor;
        private float nextRevealTime;
        private float lastRevealTime;
        private float lastSoundTime;
        private float speedScale = 1f;

        public bool IsRevealing => isRevealing;

        public DialogTextReveal(TMP_Text root, Action onComplete) {
            this.onComplete = onComplete;
            var list = new List<Target>();
            for (var t = root; t != null; t = t.linkedTextComponent) {
                var target = new Target { Text = t };
                t.OnPreRenderText += info => OnPreRenderText(target, info);
                list.Add(target);
            }
            targets = list.ToArray();
        }

        // Hides every glyph until Begin is called
        public void Prepare() {
            isRevealing = true;
            isStarted = false;
            isSkipping = false;
            revealCursor = 0;
            revealTimes.Clear();
            targets[0].Text.havePropertiesChanged = true;
        }

        public void Begin(float now) {
            if (!isRevealing) { return; }
            // Linked components regenerate synchronously inside the root's generation
            targets[0].Text.ForceMeshUpdate();
            isStarted = true;
            nextRevealTime = now + StartDelay;
            lastRevealTime = nextRevealTime;
            lastSoundTime = 0f;
            int total = TotalChars();
            if (total == 0) {
                Finish();
                return;
            }
            float duration = 0f;
            for (int i = 0; i < total; i++) {
                duration += DelayAfter(i);
            }
            speedScale = duration > MaxDuration ? MaxDuration / duration : 1f;
        }

        // animateRest fades the remaining glyphs in quickly, otherwise they pop in immediately
        public void Complete(bool animateRest, float now) {
            if (!isRevealing) { return; }
            if (!animateRest || !isStarted) {
                Finish();
                return;
            }
            isSkipping = true;
            int total = TotalChars();
            while (revealCursor < total) {
                revealTimes.Add(now);
                revealCursor++;
            }
            lastRevealTime = now;
            nextRevealTime = now;
        }

        public void Tick(float now) {
            if (!isRevealing || !isStarted) { return; }
            int total = TotalChars();
            while (revealCursor < total && nextRevealTime <= now) {
                var ci = CharAt(revealCursor);
                revealTimes.Add(nextRevealTime);
                lastRevealTime = nextRevealTime;
                if (ci.isVisible && !isSkipping && now - lastSoundTime >= SoundInterval) {
                    lastSoundTime = now;
                    UISounds.PlayDialogType();
                }
                nextRevealTime += DelayAfter(revealCursor) * speedScale;
                revealCursor++;
            }
            if (revealCursor >= total && now >= lastRevealTime + GlyphFade) {
                Finish();
                return;
            }
            for (int i = 0; i < targets.Length; i++) {
                var target = targets[i];
                if (!CanApply(target)) { continue; }
                RestoreSnapshot(target);
                ApplyReveal(target, now);
                target.Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32
                    | TMP_VertexDataUpdateFlags.Vertices);
            }
        }

        private void Finish() {
            isRevealing = false;
            isStarted = false;
            isSkipping = false;
            for (int i = 0; i < targets.Length; i++) {
                var target = targets[i];
                if (!CanApply(target)) { continue; }
                RestoreSnapshot(target);
                target.Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32
                    | TMP_VertexDataUpdateFlags.Vertices);
            }
            onComplete?.Invoke();
        }

        private void OnPreRenderText(Target target, TMP_TextInfo info) {
            if (!isRevealing) { return; }
            TakeSnapshot(target, info);
            ApplyReveal(target, GameTimer.unscaledTime);
        }

        private static bool CanApply(Target target) {
            var t = target.Text;
            // A pending regeneration will re-snapshot through OnPreRenderText
            return target.HasSnapshot && !t.havePropertiesChanged && t.isActiveAndEnabled
                && t.textInfo.materialCount == target.MaterialCount
                // TMP clears an empty text's mesh without raising OnPreRenderText, so its
                // snapshot and mesh info are stale and uploading them resurrects old glyphs
                && t.textInfo.characterCount > 0;
        }

        // Linked components share global character indices, the owner of an index is the
        // last component in the chain that starts at or before it
        private TMP_CharacterInfo CharAt(int index) {
            for (int i = targets.Length - 1; i >= 0; i--) {
                var t = targets[i].Text;
                if (!t.isActiveAndEnabled) { continue; }
                var info = t.textInfo;
                if (index < t.firstVisibleCharacter || index >= info.characterCount) { continue; }
                return info.characterInfo[index];
            }
            return default;
        }

        private int TotalChars() {
            int total = 0;
            for (int i = 0; i < targets.Length; i++) {
                var t = targets[i].Text;
                if (!t.isActiveAndEnabled) { continue; }
                total = Math.Max(total, t.textInfo.characterCount);
            }
            return total;
        }

        private float DelayAfter(int index) {
            char c = CharAt(index).character;
            if (c == '\n') { return MajorPause; }
            if (char.IsWhiteSpace(c) || c == '\u200B') { return 0f; }
            char next = CharAt(index + 1).character;
            if (!char.IsWhiteSpace(next)) { return CharDelay; }
            switch (c) {
                case '.':
                case '!':
                case '?':
                    return MajorPause;
                case ',':
                case ';':
                case ':':
                    return MinorPause;
                default:
                    return CharDelay;
            }
        }

        private float GlyphEase(int index, float now) {
            if (index >= revealCursor) { return 0f; }
            float t = Mathf.Clamp01((now - revealTimes[index]) / GlyphFade);
            float inv = 1f - t;
            return 1f - inv * inv * inv;
        }

        private void ApplyReveal(Target target, float now) {
            var info = target.Text.textInfo;
            int count = info.characterCount;
            var chars = info.characterInfo;
            if (target.CharVertexEnd.Length < target.MaterialCount) {
                Array.Resize(ref target.CharVertexEnd, target.MaterialCount);
            }
            Array.Clear(target.CharVertexEnd, 0, target.CharVertexEnd.Length);
            for (int i = 0; i < count; i++) {
                if (!chars[i].isVisible) { continue; }
                int mi = chars[i].materialReferenceIndex;
                int vi = chars[i].vertexIndex;
                var colors = info.meshInfo[mi].colors32;
                var verts = info.meshInfo[mi].vertices;
                if (vi + 3 >= colors.Length || vi + 3 >= verts.Length) { continue; }
                // Underline and strikethrough quads start right after the character quads
                if (mi < target.CharVertexEnd.Length && vi + 4 > target.CharVertexEnd[mi]) {
                    target.CharVertexEnd[mi] = vi + 4;
                }
                float eased = GlyphEase(i, now);
                if (eased >= 1f) { continue; }
                float rise = GlyphRise * (1f - eased);
                for (int k = 0; k < 4; k++) {
                    var col = colors[vi + k];
                    col.a = (byte) (col.a * eased);
                    colors[vi + k] = col;
                    verts[vi + k].y -= rise;
                }
            }
            ApplyDecorations(target, info, count, now, underline: true);
            ApplyDecorations(target, info, count, now, underline: false);
        }

        // Underline and strikethrough are separate geometry, appended after the character
        // quads, so the character pass never touches them. Every character of one run carries
        // the vertex index where that run's quads start, which is how runs are found here
        private void ApplyDecorations(Target target, TMP_TextInfo info, int count, float now,
                bool underline) {
            var chars = info.characterInfo;
            int i = 0;
            while (i < count) {
                if (!HasDecoration(chars[i], underline)) {
                    i++;
                    continue;
                }
                int vi = DecorationVertexIndex(chars[i], underline);
                int runStart = i;
                int runEnd = i;
                while (i + 1 < count && HasDecoration(chars[i + 1], underline)
                        && DecorationVertexIndex(chars[i + 1], underline) == vi) {
                    i++;
                    runEnd = i;
                }
                i++;
                ApplyDecorationRun(target, info, vi, chars[runStart].materialReferenceIndex,
                    GlyphEase(runStart, now), GlyphEase(runEnd, now));
            }
        }

        private static bool HasDecoration(TMP_CharacterInfo ci, bool underline) {
            var flag = underline ? FontStyles.Underline : FontStyles.Strikethrough;
            return (ci.style & flag) == flag;
        }

        private static int DecorationVertexIndex(TMP_CharacterInfo ci, bool underline) {
            return underline ? ci.underlineVertexIndex : ci.strikethroughVertexIndex;
        }

        // The run is a start cap, a stretched middle and an end cap. Fading the left half with
        // the first character and the right half with the last one makes the middle quad
        // interpolate, so the line wipes in under the text as it types
        private void ApplyDecorationRun(Target target, TMP_TextInfo info, int vi, int mi,
                float easedStart, float easedEnd) {
            if (easedStart >= 1f && easedEnd >= 1f) { return; }
            if (mi >= target.MaterialCount || mi >= target.CharVertexEnd.Length) { return; }
            // Guards the assumption that a run lives in the material of its first character
            if (vi < target.CharVertexEnd[mi]
                    || vi + DecorationVerts > target.VertexCounts[mi]) {
                return;
            }
            var colors = info.meshInfo[mi].colors32;
            var verts = info.meshInfo[mi].vertices;
            for (int k = 0; k < DecorationVerts; k++) {
                float eased = k < DecorationVerts / 2 ? easedStart : easedEnd;
                var col = colors[vi + k];
                col.a = (byte) (col.a * eased);
                colors[vi + k] = col;
                verts[vi + k].y -= GlyphRise * (1f - eased);
            }
        }

        private static void TakeSnapshot(Target target, TMP_TextInfo info) {
            int materials = info.materialCount;
            if (target.Colors.Length < materials) {
                Array.Resize(ref target.Colors, materials);
                Array.Resize(ref target.Vertices, materials);
                Array.Resize(ref target.VertexCounts, materials);
            }
            for (int m = 0; m < materials; m++) {
                var mesh = info.meshInfo[m];
                int n = Math.Min(mesh.vertexCount,
                    Math.Min(mesh.colors32.Length, mesh.vertices.Length));
                if (target.Colors[m] == null || target.Colors[m].Length < n) {
                    target.Colors[m] = new Color32[mesh.colors32.Length];
                    target.Vertices[m] = new Vector3[mesh.vertices.Length];
                }
                Array.Copy(mesh.colors32, target.Colors[m], n);
                Array.Copy(mesh.vertices, target.Vertices[m], n);
                target.VertexCounts[m] = n;
            }
            target.MaterialCount = materials;
            target.HasSnapshot = true;
        }

        private static void RestoreSnapshot(Target target) {
            var info = target.Text.textInfo;
            for (int m = 0; m < target.MaterialCount; m++) {
                var mesh = info.meshInfo[m];
                int n = Math.Min(target.VertexCounts[m],
                    Math.Min(mesh.colors32.Length, mesh.vertices.Length));
                Array.Copy(target.Colors[m], mesh.colors32, n);
                Array.Copy(target.Vertices[m], mesh.vertices, n);
            }
        }
    }
}
