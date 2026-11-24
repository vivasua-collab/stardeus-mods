using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Input;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Hints {
    public static class LoadHints {
        [SelfInit]
        private static void Cleanup() {
            isDatabaseBuilt = false;
        }
        public static readonly List<string> AllHints = new();
        private static Queue<string> HintQueue;
        public static string NextHint(int attemptsLeft = 10) {
            if (HintQueue == null || HintQueue.Count == 0) {
                if (AllHints.Count == 0) {
                    return "Don't forget to stop and smell the roses";
                }
                HintQueue = new(AllHints.OrderBy(_ => Rng.UValue));
            }
            var hint = HintQueue.Dequeue();
            if (attemptsLeft > 0 && VirtualMouseInput.IsCurrent
                    && hint.Contains("Glyphs_Keyboard")) {
                return NextHint(attemptsLeft--);
            }
            if (attemptsLeft < 1) {
                D.Err("Exhausted all attempts to show hint for controller");
            }
            return hint;
        }

        private static bool isDatabaseBuilt;

        public static void BuildDatabase() {
            if (isDatabaseBuilt) { return; }
            AllHints.Clear();
            try {
                AllHints.Add(T.HintEl_Drag_Connect);
                AllHints.Add(T.HintEl_Disconnect);
                AllHints.Add(T.HintConstruction_Cramped);
                AllHints.Add(T.HintStorage_Auto_Haul);

                int i = 0;
                int attempts = 99999;
                int gap = 0;
                int dynamicHints = 0;
                while (attempts-- > 0) {
                    var hintKey = $"global.hint.{i++}";
                    if (hintKey.HasT()) {
                        if (gap > 0) {
                            #if !PRODUCTION_BUILD
                            D.Warn("There is a gap before hint key: {0}", hintKey);
                            #endif
                            gap = 0;
                        }
                        dynamicHints++;
                        AllHints.Add(Strings.ParseSteamNews(hintKey.T(), Texts.TMPColorAccented));
                    } else if (gap++ > 3) {
                        break;
                    }
                }

                // D.Log("Loaded {0} hints. {1} dynamic.", AllHints.Count, dynamicHints);
                isDatabaseBuilt = true;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to build hint database");
            }
        }
    }
}
