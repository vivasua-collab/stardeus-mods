using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Minigames {
    // Owns the single minigame session: state keeping, save/load, the update
    // loop and input edge detection. Only one minigame is ever displayed; the
    // WGameApp window and its RenderTexture are reused across games.
    public sealed class MinigameSys : GameSystem, ISaveableSpecial {
        public const string SysId = "Minigames";
        public override string Id => SysId;

        // GameTimer.unscaledDeltaTime is not capped by Time.maximumDeltaTime,
        // so clamp it at the source: after an alt-tab or a debugger break it
        // can be seconds long and would advance dozens of steps in one frame.
        private const float MaxCatchUpDt = 0.25f;

        private Minigame active;
        private readonly Dictionary<string, MinigameState> savedStates = new();
        private readonly Dictionary<string, int> highScores = new();
        private MinigameWindow window;
        private readonly MinigameDirInput dirInput = new();
        private bool wasFocused;
        private int lastShownScore = -1;
        private bool titleShowsDead;
        private string activeAppName;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new MinigameSys());
        }

        protected override void OnInitialize() {
            S.UIQuery.ToggleMinigame.SetResponder(ToggleSession);
            S.Sig.OnLateUpdate.AddListener(OnLateUpdate);
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReloaded);
        }

        public override void Unload() {
            // The.SysSig listeners must be removed here (they survive state
            // changes); S.Sig.OnLateUpdate is a GameSig and gets wiped as a
            // whole, together with its responders.
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReloaded);
            // Lifetime.DisposeAll runs before systems unload when exiting to
            // the main menu, so this CloseSession usually finds the window
            // already disposed - it must stay null/disposed-safe.
            if (active != null) {
                CloseSession();
            }
            savedStates.Clear();
        }

        private void OnThemeReloaded() {
            active?.OnThemeColorsChanged();
        }

        private bool ToggleSession(string appId) {
            if (active != null && active.Id == appId) {
                CloseSession();
                return true;
            }
            OpenSession(appId);
            return true;
        }

        private void OpenSession(string appId) {
            StoreOrDiscard(active);
            var minigame = CreateMinigame(appId);
            if (minigame == null) {
                D.Err("Unknown minigame id: {0}", appId);
                return;
            }
            var app = Registries.ShipComputerApps.Get(appId, false);
            activeAppName = app != null ? app.Name : appId;
            active = minigame;
            active.OnThemeColorsChanged();

            if (window == null || !window.IsOpen) {
                window = new MinigameWindow();
                window.Open(S, activeAppName, CloseSession, RestartSession);
            }

            if (savedStates.Remove(appId, out var state)) {
                active.Resume(state, window.Target);
            } else {
                active.StartNew(window.Target);
            }
            lastShownScore = -1;
            titleShowsDead = false;
            wasFocused = false;
            UpdateTitle(true);
            window.SetAsLastSibling();
            UIShowing.Set(UIShowingFlags.Minigame, true);
            active.Render();
        }

        // Footer "Restart Now" action. Only reachable while the window is open;
        // clicking it raises it to top, so normal focus handling resumes sim.
        private void RestartSession() {
            if (active == null || window == null || !window.IsOpen) { return; }
            StoreOrDiscard(active);
            active.StartNew(window.Target);
            lastShownScore = -1;
            titleShowsDead = false;
            UpdateTitle(true);
            active.Render();
        }

        private void CloseSession() {
            StoreOrDiscard(active);
            active = null;
            window?.Close();
            window = null;
            UIShowing.Set(UIShowingFlags.Minigame, false);
            UIShowing.MinigameConsumesMovement = false;
            wasFocused = false;
        }

        // The single place where requirement "game over -> fresh board on next
        // open" lives: dead games are discarded, alive ones are stored so they
        // resume after an app switch or a close/reopen.
        private void StoreOrDiscard(Minigame m) {
            if (m == null) { return; }
            highScores.TryGetValue(m.Id, out var best);
            if (m.Score > best) { best = m.Score; }
            highScores[m.Id] = best;
            if (m.IsDead) {
                savedStates.Remove(m.Id);
            } else {
                savedStates[m.Id] = m.CaptureState();
            }
        }

        private static Minigame CreateMinigame(string appId) {
            switch (appId) {
                case MinigameSnake.SnakeId:
                    return new MinigameSnake();
                case MinigameBreakout.BreakoutId:
                    return new MinigameBreakout();
                case MinigameStacker.StackerId:
                    return new MinigameStacker();
                case Minigame2048.Game2048Id:
                    return new Minigame2048();
                default:
                    return null;
            }
        }

        private void OnLateUpdate() {
            if (active == null) { return; }

            // Poll every frame regardless of focus so the edge baseline stays
            // fresh: holding a key while unfocused must not fire a spurious
            // turn on the first focused frame. Bindings.GetValue returns
            // default while locked (e.g. console), which covers typing.
            // VMouseCycleUI is the gamepad D-pad; VirtualMouseInput leaves it
            // to the game while MinigameConsumesMovement is set.
            var raw = The.Bindings.GetValue<Vector2>(ActionType.CameraMove)
                + The.Bindings.GetValue<Vector2>(ActionType.QuickSearchNav)
                + The.Bindings.GetValue<Vector2>(ActionType.VMouseCycleUI);

            var focused = window != null && window.IsFocused;
            UIShowing.MinigameConsumesMovement = focused && !active.IsDead;
            if (!focused) {
                if (wasFocused) {
                    // just lost focus - freeze the frame under a paused overlay
                    wasFocused = false;
                    active.SetPaused(true);
                    active.Render();
                }
                dirInput.Sync(raw);
                return;
            }
            if (!wasFocused) {
                wasFocused = true;
                dirInput.Sync(raw);
                active.SetPaused(false);
                // Rendering is state-change driven, so repaint on refocus in
                // case the RT lost its contents (alt-tab, device reset).
                active.Render();
            }
            if (dirInput.Update(raw, out var turnDir)) {
                active.OnInput(turnDir);
            }
            // Held-state input for games that need it (Breakout paddle);
            // no-op for edge-based games like Snake.
            active.OnRawMove(raw);

            if (active.IsDead) { return; }
            if (active.Step(Maths.Min(GameTimer.unscaledDeltaTime, MaxCatchUpDt))) {
                active.Render();
                UpdateTitle(false);
            }
        }

        private void UpdateTitle(bool force) {
            if (!force && active.Score == lastShownScore
                    && titleShowsDead == active.IsDead) {
                return;
            }
            lastShownScore = active.Score;
            titleShowsDead = active.IsDead;
            highScores.TryGetValue(active.Id, out var best);
            if (active.Score > best) {
                best = active.Score;
                // Persist immediately: StoreOrDiscard only runs on close /
                // switch, but the player (or an autosave) may save while the
                // finished board is still on screen.
                highScores[active.Id] = best;
            }
            window.SetTitle(active.IsDead
                ? T.MinigameTitleGameover_V3.F(activeAppName, active.Score, best)
                : T.MinigameTitleScore_V2.F(activeAppName, active.Score));
            // Synced on every actual title change: hidden while running /
            // paused, shown with the restart button once game over is reached.
            window.SetGameOver(active.IsDead);
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Minigames = new MinigameSysData {
                HighScores = new Dictionary<string, int>(highScores),
            };
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            highScores.Clear();
            var d = sd?.Minigames;
            if (d?.HighScores == null) { return; }
            foreach (var kv in d.HighScores) {
                highScores[kv.Key] = kv.Value;
            }
        }
    }
}
