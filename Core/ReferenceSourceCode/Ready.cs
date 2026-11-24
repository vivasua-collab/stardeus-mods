#if !PRODUCTION_BUILD
// #define DEBUG_WHEN
// #define DEBUG_READY
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Console;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game {
    /// <summary>
    /// Part of this class is legacy, nowadays only Ready.Set and Ready.When
    /// is used when something new has to be added to this.
    /// </summary>
    public static class Ready {

        public struct WhenCondition {
            public int Id;
            public bool IsTrue;
            public int Pending;
            public event Action WhenTrue;
            #if DEBUG_WHEN
            public List<string> ActionsTraces;
            #endif
            // Returns the actions to run now that the condition became true. The caller
            // stores this struct back before running them.
            public Action Set(bool val) {
                #if DEBUG_WHEN
                D.Warn("Setting when: {0} -> {1}", WhenH.ResolveName(Id), val);
                #endif
                if (IsTrue && !val) {
                    D.Err("Setting condition True -> False! {0}", WhenH.ResolveName(Id));
                }
                if (IsTrue == val) {
                    if (val) {
                        D.Err("Setting condition to true more than once!");
                    }
                    return null;
                }
                IsTrue = val;
                if (!val) { return null; }
                var actions = WhenTrue;
                WhenTrue = null;
                #if DEBUG_WHEN
                ActionsTraces = null;
                #endif
                Pending = 0;
                return actions;
            }
            public bool Add(Action act) {
                #if DEBUG_WHEN
                D.Warn("Adding when: {0} -> {1}", WhenH.ResolveName(Id),
                    act?.Target?.GetType()?.FullName);
                #endif
                if (IsTrue) {
                    act();
                    return false;
                } else {
                    Pending++;
                    WhenTrue += act;
                    #if DEBUG_WHEN
                    ActionsTraces ??= new List<string>();
                    ActionsTraces.Add(Environment.StackTrace);
                    #endif
                    return true;
                }
            }

            public override string ToString() {
                #if DEBUG_WHEN
                if (ActionsTraces != null) {
                    return $"Pending:{Pending}|{string.Join(", ", ActionsTraces)}";
                }
                #endif
                return $"Pending:{Pending}";
            }
        }

        // If you'll ever add new onSomethingReady, nullify it on unready!!!
        private static event Action onRenderingReady;
        private static event Action onBeforeCoreReady;
        private static event Action onCoreReady;
        private static event Action onPathReady;
        private static event Action onGameReady;
        private static event Action onGameReadyLate;
        private static event Action onMainThread;
        private static readonly object mainThreadLock = new();
        public static void WhenMainThread(object requester, string why, Action action) {
            //D.Err("Put FlushMainThread in Bootstrap.Update if you want to use this");
            if (A.IsMainThread) {
                MiscUtils.TryExecute(action, "Failed invoking Ready.WhenMainThread");
                return;
            }
            lock (mainThreadLock) {
                onMainThread += action;
            }
        }

        public static void FlushOnMainThread() {
            lock (mainThreadLock) {
                if (onMainThread != null) {
                    var actions = onMainThread;
                    onMainThread = null;
                    InvokeEach(actions, "Failed invoking Ready.FlushOnMainThread");
                }
            }
        }

        // One handler per try and the list cleared first: invoking the multicast delegate
        // stopped at the first throwing handler, dropped the ones after it, and where it
        // was not wrapped at all the throw also kept the list for a second run
        private static void InvokeEach(Action actions, string errMsg) {
            if (actions == null) { return; }
            var list = actions.GetInvocationList();
            for (int i = 0; i < list.Length; i++) {
                MiscUtils.TryExecute((Action) list[i], errMsg);
            }
        }

        public static void WhenGame(object requester, string why, Action action) {
            if (Game) {
                MiscUtils.TryExecute(action, "Failed invoking Ready.WhenGame");
                action = null;
            } else {
                #if DEBUG_READY
                if (onGameReady != null) {
                    if (Arrays.Contains(onGameReady.GetInvocationList(), action)) {
                        D.Err("Ready.WhenGame already contains {0}, {1}",
                            why, requester);
                    }
                }
                #endif
                onGameReady += action;
            }
        }

        ///<summary>This will execute after whenGame does. To prevent race conditions some things might want to use this queue instead of WhenGame</summary>
        public static void WhenGameLate(object requester, string why, Action action) {
            if (Game) {
                MiscUtils.TryExecute(action, "Failed invoking Ready.WhenGameLate");
                action = null;
            } else {
                #if DEBUG_READY
                if (onGameReadyLate != null) {
                    if (Arrays.Contains(onGameReadyLate.GetInvocationList(), action)) {
                        D.Err("Ready.WhenGame already contains {0}, {1}",
                            why, requester);
                    }
                }
                #endif
                onGameReadyLate += action;
            }
        }

        public static void BeforeReadyCore(Action action) {
            if (Core) {
                D.Err("This cannot be invoked after Ready.Core!");
                return;
            }
            onBeforeCoreReady += action;
        }

        public static void RunBeforeReadyCore() {
            var actions = onBeforeCoreReady;
            onBeforeCoreReady = null;
            InvokeEach(actions, "Failed invoking Ready.RunBeforeReadyCore");
        }

        public static void WhenCore(Action action) {
            if (Core) {
                MiscUtils.TryExecute(action, "Failed invoking Ready.WhenCore");
            } else {
                onCoreReady += action;
            }
        }

        public static void WhenPathfinding(Action action) {
            if (Pathfinding) {
                MiscUtils.TryExecute(action, "Failed invoking Ready.WhenPathfinding");
            } else {
                onPathReady += action;
            }
        }

        public static bool Energy;
        public static string EnergyWhy;
        public static bool Core;
        public static string CoreWhy;
        public static bool Mods;
        public static string ModsWhy;
        public static bool Game;
        public static string GameWhy;
        public static bool StateLock;
        public static string StateLockWhy;
        public static bool Save;
        public static string SaveWhy;
        public static bool SaveIO;
        public static string SaveIOWhy;
        public static bool Gen;
        public static string GenWhy;
        public static bool CmdQ;
        public static string CmdQWhy;
        public static bool AtmoO2;
        public static bool AtmoTemp;
        public static bool Rendering;
        public static string RenderingWhy;
        public static bool Areas;
        public static bool AreasInitial;
        public static bool Pathfinding;
        public static string AreasWhy;
        public static string AreasInitialWhy;
        public static bool TextInput;
        /// <summary>
        /// NO LONGER TRUE!
        /// FIXME this is also used in ResearchWidget while it's loading, to prevent hitting ESC
        /// </summary>
        public static bool TextInputFocus;
        public static TMP_InputField TextInputFocusField;
        public static string TextInputWhy;
        public static string TextInputFocusWhy;
        public static bool UIOverlay;
        public static bool UserInput;
        public static string UserInputWhy;
        private static readonly Dictionary<int, WhenCondition> whenConditions = new();

        public static void Set(int cond, bool val) {
            if (!whenConditions.TryGetValue(cond, out var when)) {
                when = new WhenCondition();
                when.Id = cond;
            }
            var actions = when.Set(val);
            // Stored before the actions run: they used to run first, so an action that
            // registered for the same condition went into the stale dictionary copy and was
            // overwritten by this write-back, never to run
            whenConditions[cond] = when;
            if (actions != null) {
                InvokeEach(actions,
                    $"Failed invoking Ready.Set(WhenH.({WhenH.ResolveName(cond)}))");
            }
        }
        public static bool Get(int cond) {
            if (!whenConditions.TryGetValue(cond, out var when)) {
                return false;
            }
            return when.IsTrue;
        }

        /// <summary>
        /// Will execute action either immediately (if ready), or when Ready.Set(cond, true)
        /// will be called.
        /// </summary>
        /// <param name="cond">WhenH.SomeId</param>
        /// <param name="act"></param>
        public static void When(int cond, Action act) {
            if (!whenConditions.TryGetValue(cond, out var when)) {
                when = new WhenCondition();
                when.Id = cond;
            }
            if (when.Add(act)) {
                whenConditions[cond] = when;
            }
        }

        public static bool Input => UserInput && Game && Gen && Save
            && TextInput && !InGameConsole.IsVisible && !UIShowing.MainMenu;
        public static bool ShowLoading => !Core || !Mods || !Gen || !Save /*|| !Rendering*/;
        public static bool Everything => Core && Mods && Gen && Save && Rendering
            && Areas && Energy && Pathfinding;

        public static void SetCore(bool status, string why) {
            var wasTrue = Core;
            Core = status;
            CoreWhy = why;
            if (!wasTrue && status && onCoreReady != null) {
                var actions = onCoreReady;
                onCoreReady = null;
                InvokeEach(actions, "Failed invoking Ready.SetCore");
            }
        }

        public static void SetSave(bool status, string why) {
            Save = status;
            SaveWhy = why;
        }
        public static void SetStateLock(bool status, string why) {
            StateLock = status;
            StateLockWhy = why;
        }
        public static void SetEnergy(bool status, string why) {
            Energy = status;
            EnergyWhy = why;
        }
        public static void SetUserInput(bool status, string why) {
            UserInput = status;
            UserInputWhy = why;
        }
        public static void SetTextInput(bool status, string why) {
            // D.Warn("Setting text input: {0}->{1}", status, why);
            TextInput = status;
            TextInputWhy = why;
        }
        public static void SetTextInputFocus(bool status, TMP_InputField field, string why) {
            #if !PRODUCTION_BUILD
            D.Warn("Setting text input focus: {0}->{1}", status, why);
            #endif
            if (status) {
                if (field != null && field != TextInputFocusField) {
                    D.Err("Trying to set TextInputFocus to true while field does not match! {0} != {1}",
                        D.GOPath(field.gameObject.transform),
                        D.GOPath(TextInputFocusField?.gameObject.transform));
                    return;
                }
                TextInputFocusField = null;
            } else {
                TextInputFocusField = field;
            }
            TextInputFocus = status;
            TextInputFocusWhy = why;
        }

        public static void SetSaveIO(bool status, string why) {
            SaveIO = status;
            SaveIOWhy = why;
        }

        public static void UnreadyGame() {
            Energy = false;
            Game = false;
            Rendering = false;
            Areas = false;
            AreasInitial = false;
            Pathfinding = false;
            Gen = true;
            Save = true;
            StateLock = true;
            SaveIO = true;
            CmdQ = true;
            AtmoO2 = true;
            AtmoTemp = true;
            TextInput = true;
            TextInputFocus = true;
            TextInputFocusField = null;
            UserInput = true;
            onRenderingReady = null;
            onCoreReady = null;
            onGameReady = null;
            onGameReadyLate = null;
            onMainThread = null;
            // a load aborted before pathfinding was ready left these to fire in the next game
            onPathReady = null;
            if (whenConditions.Count > 0) {
                foreach (var w in whenConditions) {
                    if (w.Value.Pending != 0) {
                        D.Err("Clearing non-executed when condition: {0}: {1}",
                            WhenH.ResolveName(w.Key), w.Value);
                    }
                }
            }
            whenConditions.Clear();
        }

        [ExecuteOnReload]
        public static void Unready() {
            //// D.Log("Unreadying ready");
            Core = false;
            Mods = false;
            onBeforeCoreReady = null;
            UnreadyGame();
        }

        public static void SetMods(bool status, string why) {
            Mods = status;
            ModsWhy = why;
        }
        public static void SetGame(bool status, string why) {
            var wasGame = Game;
            Game = status;
            GameWhy = why;
            if (!wasGame && status) {
                var actions = onGameReady;
                onGameReady = null;
                InvokeEach(actions, "Failed invoking Ready.SetGame");
                var lateActions = onGameReadyLate;
                onGameReadyLate = null;
                InvokeEach(lateActions, "Failed invoking Ready.SetGame (late)");
            }
        }

        public static void SetRendering(bool status, string why) {
            var wasRen = Rendering;
            Rendering = status;
            RenderingWhy = why;
            if (!wasRen && status) {
                if (onRenderingReady != null) {
                    #if !PRODUCTION_BUILD
                    // D.Warn("On rendering ready count: {0}",
                    //    onRenderingReady.GetInvocationList().Length);
                    #endif
                    var actions = onRenderingReady;
                    onRenderingReady = null;
                    InvokeEach(actions, "Failed invoking Ready.SetRendering");
                }
            }
        }
        public static void ClearOnRendering() {
            onRenderingReady = null;
        }

        public static void SetGen(bool status, string why) {
            Gen = status;
            GenWhy = why;
        }

        public static void SetAreas(bool status, string why) {
            Areas = status;
            AreasWhy = why;
        }
        public static void SetAreasInitial(bool status, string why) {
            AreasInitial = status;
            AreasWhy = why;
        }
        public static void SetPathfinding(bool status) {
            Pathfinding = status;
            if (status) {
                var actions = onPathReady;
                onPathReady = null;
                InvokeEach(actions, "Failed invoking Ready.SetPathfinding");
            }
        }
        public static void SetCmdQ(bool status, string why) {
            CmdQ = status;
            CmdQWhy = why;
        }

        public static IEnumerator WaitForSaveFull(object waiter) {
            while (!Save || !SaveIO) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for save: {1} {2}", waiter, SaveWhy, SaveIOWhy);
                }
                #endif
                yield return null;
            }
        }

        public static IEnumerator WaitForSaveIO(object waiter) {
            while (!SaveIO) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for save i/o: {1}", waiter, SaveIOWhy);
                }
                #endif
                yield return null;
            }
        }
        public static IEnumerator WaitForCmdQ(object waiter) {
            while (!CmdQ) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for cmd q: {1}", waiter, CmdQWhy);
                }
                #endif
                yield return null;
            }
        }
        public static IEnumerator WaitForMods(object waiter) {
            while (!Mods) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for mods: {1}", waiter, ModsWhy);
                }
                #endif
                yield return null;
            }
        }
        public static IEnumerator WaitForEnergy(object waiter) {
            while (!Energy) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for energy: {1}", waiter, GenWhy);
                }
                #endif
                yield return null;
            }
        }
        public static IEnumerator WaitForGen(object waiter) {
            while (!Gen) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for gen: {1}", waiter, GenWhy);
                }
                #endif
                yield return null;
            }
        }
        public static IEnumerator WaitForGame(object waiter, GameRuntime r = null) {
            while (!Game) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for game: {1}", waiter, GameWhy);
                }
                #endif
                yield return null;
                if (r != null && !r.IsCurrent) {
                    #if DEBUG_READY
                    D.Err("Waited for game but runtime now not current: {0}", waiter);
                    #endif
                    yield break;
                }
            }
        }
        public static IEnumerator WaitForInput(object waiter) {
            while (!Input) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for input: {1}", waiter,
                       ShortStatus);
                }
                #endif
                yield return null;
            }
        }
        public static IEnumerator WaitForRendering(object waiter) {
            while (!Rendering) {
                if (G.ClockSpeed > ClockSpeed.Fast2) {
                    break;
                }
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for rendering: {1}", waiter,
                       ShortStatus);
                }
                #endif
                yield return null;
            }
        }

        /*
        public static IEnumerator WaitForAreas(object waiter) {
            while (!Areas) {
                #if DEBUG_READY
                if (UnityEngine.Time.frameCount % 600 == 0) {
                    D.Log("{0} still waiting for areas: {1}", waiter, AreasWhy);
                }
                #endif
                yield return null;
            }
        }
        */

        public static string ShortStatus {
            get {
                var sb = new StringBuilder();
                if (!Core) { sb.Append('C'); }
                if (!Mods) { sb.Append('M'); }
                if (!Rendering) { sb.Append('R'); }
                if (!CmdQ) { sb.Append('Q'); }
                if (!Energy) { sb.Append('#'); }
                if (!Game) { sb.Append('G'); }
                if (!Gen) { sb.Append('@'); }
                if (!Save) { sb.Append('S'); }
                if (!SaveIO) { sb.Append('s'); }
                if (!Areas) { sb.Append('A'); }
                if (!TextInput) { sb.Append('T'); }
                if (!UserInput) { sb.Append('U'); }
                if (!TextInputFocus) { sb.Append('t'); }
                return sb.ToString();
            }
        }
        public static Dictionary<string, string> Reasons {
            get {
                var d = new Dictionary<string, string>();
                if (!Core) { d.Add("Core", CoreWhy); }
                if (!Mods) { d.Add("Mods", ModsWhy); }
                if (!Rendering) { d.Add("Rendering", RenderingWhy); }
                if (!Game) { d.Add("Game", GameWhy); }
                if (!CmdQ) { d.Add("CmdQ", CmdQWhy); }
                if (!Energy) { d.Add("Energy", EnergyWhy); }
                if (!Gen) { d.Add("Gen", GenWhy); }
                if (!Save) { d.Add("Save", SaveWhy); }
                if (!SaveIO) { d.Add("Save I/O", SaveIOWhy); }
                if (!Areas) { d.Add("Areas", AreasWhy); }
                if (!TextInput) { d.Add("TextInput", TextInputWhy); }
                if (!TextInputFocus) { d.Add("TextInputFocus", TextInputFocusWhy); }
                return d;
            }
        }
        public static void Toggle(string key) {
            switch (key) {
                case "Core" : SetCore(!Core, "Editor toggle"); break;
                case "Mods" : SetMods(!Mods, "Editor toggle"); break;
                case "Rendering" : SetRendering(!Rendering, "Editor toggle"); break;
                case "Game" : SetGame(!Game, "Editor toggle"); break;
                case "Gen" : SetGen(!Gen, "Editor toggle"); break;
                case "Save" : SetSave(!Save, "Editor toggle"); break;
                case "Save I/O" : SetSaveIO(!SaveIO, "Editor toggle"); break;
                case "Areas" : SetAreas(!Areas, "Editor toggle"); break;
                case "TextInput" : SetTextInput(!TextInput, "Editor toggle"); break;
                case "TextInputFocus" : SetTextInputFocus(!TextInputFocus, null, "Editor toggle"); break;
            }
        }

        public static string LongStatus {
            get {
                var sb = new StringBuilder();
                sb.Append("Ready[");
                if (!Core) {
                    sb.AppendFormat("Core: {0}\n", CoreWhy);
                }
                if (!Mods) {
                    sb.AppendFormat("Mods: {0}\n", ModsWhy);
                }
                if (!Rendering) {
                    sb.AppendFormat("Rendering: {0}\n", RenderingWhy);
                }
                if (!Pathfinding) {
                    sb.AppendFormat("Path: ?\n");
                }
                if (!Game) {
                    sb.AppendFormat("Game: {0}\n", GameWhy);
                }
                if (!Gen) {
                    sb.AppendFormat("Gen: {0}\n", GenWhy);
                }
                if (!Save) {
                    sb.AppendFormat("Save: {0}\n", SaveWhy);
                }
                if (!SaveIO) {
                    sb.AppendFormat("Save I/O: {0}\n", SaveIOWhy);
                }
                if (!StateLock) {
                    sb.AppendFormat("StateLock: {0}\n", StateLockWhy);
                }
                if (!Areas) {
                    sb.AppendFormat("Areas: {0}\n", AreasWhy);
                }
                if (!UserInput) {
                    sb.AppendFormat("UserInput: {0}\n", UserInputWhy);
                }
                if (!TextInput) {
                    sb.AppendFormat("TextInput: {0}\n", TextInputWhy);
                }
                if (!TextInputFocus) {
                    sb.AppendFormat("TextInputFocus: {0}\n", TextInputFocusWhy);
                }
                if (whenConditions.Count > 0) {
                    sb.Append("WhenConditions:\n");
                    foreach (var kvp in whenConditions) {
                        sb.AppendFormat("{0}: {1}\n", WhenH.ResolveName(kvp.Key), kvp.Value);
                    }
                } else {
                    sb.Append("WhenConditions: None\n");
                }

                sb.Append("]");
                return sb.ToString();
            }
        }
    }
}
