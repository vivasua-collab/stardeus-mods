using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using UnityEngine;

namespace Game.Visuals {
    // Beings brushing past plants (Ambient Motion setting): every step a walking being takes
    // next to a planter is remembered for a moment, and the plant sway shader (GPUR/SpriteSway)
    // pushes nearby plants along the step and lets them spring back. Beings standing among
    // plants (harvesting, tending) keep them rustling with weaker pushes. A shader has no
    // memory, so the pushes live here, as a small ring uploaded only when it changes.
    // See Docs/Architecture/Systems/AmbientMotion.md.
    public static class PlantBrush {
        // Must match the _PlantPushes array in GPUSpriteSway.shader (two entries per push)
        public const int Capacity = 64;
        public const float WalkStrength = 1f;
        public const float StandStrength = 0.4f;
        // How often a standing being rustles the plants around it, in ambient seconds
        private const float StandInterval = 0.6f;
        // Unity fixes a global array's length on first set, so unused slots carry a time this
        // far in the past that the shader skips them
        private const float Expired = -1000f;

        // Per push: [2i] = (x, y, time on the AmbientMotion clock, strength),
        // [2i + 1] = (direction x, direction y, 0, 0)
        private static readonly Vector4[] pushes = new Vector4[Capacity * 2];
        private static int next;
        private static int count;
        private static bool isDirty;
        private static float nextStandScan;
        private static GameState recordedFor;

        public static int Count => count;
        public static Vector4 LatestPlace => pushes[Latest * 2];
        public static Vector2 LatestDirection => pushes[Latest * 2 + 1];
        private static int Latest => (next + Capacity - 1) % Capacity;

        [ExecuteOnReload]
        private static void ClearState() {
            Clear();
            recordedFor = null;
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void ApplyOnBoot() {
            Clear();
            Upload();
        }

        public static void Clear() {
            for (int i = 0; i < Capacity; i++) {
                pushes[i * 2] = new Vector4(0f, 0f, Expired, 0f);
                pushes[i * 2 + 1] = Vector4.zero;
            }
            next = 0;
            count = 0;
            nextStandScan = 0f;
            isDirty = true;
        }

        // From BeingGraphicsComp about once per tile a walking being walks
        public static void OnStep(Vector2 pos, Vector2 dir) {
            if (!AmbientMotion.IsOn || dir == Vector2.zero) { return; }
            if (!HasPlantsNear(pos)) { return; }
            Record(pos, dir.normalized, WalkStrength);
        }

        // Standing beings get no movement updates, so they are scanned instead: alive,
        // upright (not lying in bed or dead), not hidden, and next to plants
        public static void ScanStanding() {
            var s = A.S;
            if (s == null || !AmbientMotion.IsOn) { return; }
            var beings = s.Beings.All;
            for (int i = 0; i < beings.Count; i++) {
                var b = beings[i];
                if (!b.IsAlive || b.Motion == null || b.Motion.VelocityPercent > 0f) { continue; }
                if (b.Graphics == null || !b.Graphics.IsVertical) { continue; }
                if (b.Data.HasStateFlag(BeingState.Hidden)) { continue; }
                if (!HasPlantsNear(b.Position)) { continue; }
                var angle = Rng.URange(-Mathf.PI, Mathf.PI);
                Record(b.Position, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), StandStrength);
            }
        }

        private static void Record(Vector2 pos, Vector2 dir, float strength) {
            ForgetOtherGames();
            recordedFor = A.S;
            pushes[next * 2] = new Vector4(pos.x, pos.y, AmbientMotion.Time, strength);
            pushes[next * 2 + 1] = new Vector4(dir.x, dir.y, 0f, 0f);
            next = (next + 1) % Capacity;
            if (count < Capacity) {
                count++;
            }
            isDirty = true;
        }

        // A planter growing something in the 3x3 tiles around the being (harvesting works
        // from a neighbouring tile). IsConfigured, not CurrentPlants: that array is filled
        // lazily and stays empty in most loaded planters.
        private static bool HasPlantsNear(Vector2 pos) {
            var cx = (int) pos.x;
            var cy = (int) pos.y;
            for (int x = cx - 1; x <= cx + 1; x++) {
                for (int y = cy - 1; y <= cy + 1; y++) {
                    if (Pos.IsOOBXY(x, y)) { continue; }
                    var obj = EntityUtils.ParentObjAt(Pos.FromXY(x, y));
                    if (obj != null && obj.TryGetComponent<PlanterComp>(out var planter)
                            && planter.IsConfigured) {
                        return true;
                    }
                }
            }
            return false;
        }

        // Pushes recorded in another game (before a load) are at the wrong places
        private static void ForgetOtherGames() {
            if (recordedFor != null && recordedFor != A.S) {
                Clear();
                recordedFor = null;
            }
        }

        // Every frame from AmbientMotion, also while paused (a save loads paused)
        public static void Update(bool isRunning) {
            ForgetOtherGames();
            if (isRunning && AmbientMotion.Time >= nextStandScan) {
                nextStandScan = AmbientMotion.Time + StandInterval;
                ScanStanding();
            }
            if (!isDirty) { return; }
            isDirty = false;
            Shader.SetGlobalVectorArray(ShaderVars._PlantPushes, pushes);
        }

        private static void Upload() {
            isDirty = true;
            Update(false);
        }
    }
}
