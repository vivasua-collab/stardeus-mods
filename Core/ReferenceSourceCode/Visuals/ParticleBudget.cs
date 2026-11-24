#if !PRODUCTION_BUILD
// #define TEST_BUDGET_CUTS
// #define DEBUG_PARTICLE_BUDGET
#endif
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    public static class ParticleBudget {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize() {
            activeCount = 0;
            maxBudget = -1;
            maxBudgetPerType = -1;
            countsByType.Clear();
            vfxDisabledByTunableCache.Clear();
        }

        private static int activeCount;
        private static int maxBudget;
        private static int maxBudgetPerType;
        private static readonly Dictionary<string, int> countsByType = new();
        private static readonly Dictionary<string, bool> vfxDisabledByTunableCache = new();

        public static int ActiveCount => activeCount;
        public static int MaxBudget => maxBudget;
        public static int MaxBudgetPerType => maxBudgetPerType;
        public static bool IsExhausted => activeCount >= maxBudget;
        public static IReadOnlyDictionary<string, int> CountsByType => countsByType;

        public static void SetBudget(int budget, int perType) {
            maxBudget = budget;
            maxBudgetPerType = perType;
        }

        public static bool TryAcquire(string prefabName) {
            #if TEST_BUDGET_CUTS
            if (Rng.UChance(0.5f)) {
                return false;
            }
            #endif
            if (prefabName != null && countsByType.TryGetValue(prefabName, out var countByType)
                && countByType >= maxBudgetPerType) {
                #if DEBUG_PARTICLE_BUDGET
                D.Log("Failed to acquire particle by type cap: {0}, typeCount: {1}, typeBudget: {2}",
                    prefabName, countByType, maxBudgetPerType);
                #endif
                return false;
            }

            if (activeCount >= maxBudget) {
                #if DEBUG_PARTICLE_BUDGET
                D.Log("Failed to acquire particle: {0}, active: {1}, budget: {2}",
                    prefabName, activeCount, maxBudget);
                #endif
                return false;
            }
            #if DEBUG_PARTICLE_BUDGET
            D.Log("Acquiring particle: {0}, active: {1}, budget: {2}",
                prefabName, activeCount, maxBudget);
            #endif
            activeCount++;
            countsByType.TryGetValue(prefabName, out var count);
            countsByType[prefabName] = count + 1;
            return true;
        }

        public static void Release(string prefabName) {
            #if DEBUG_PARTICLE_BUDGET
            D.Log("Releasing particle: {0}, active: {1}, budget: {2}",
                prefabName, activeCount, maxBudget);
            #endif
            if (activeCount <= 0) {
                #if !PRODUCTION_BUILD
                if (countsByType.Count > 0) {
                    D.Err("Active particle count is already zero when trying to release. This indicates a mismatch between acquire and release calls! {0}", prefabName);
                }
                #endif
                return;
            }
            activeCount--;
            if (countsByType.TryGetValue(prefabName, out var count)) {
                if (count <= 1) {
                    countsByType.Remove(prefabName);
                } else {
                    countsByType[prefabName] = count - 1;
                }
            } else {
                #if !PRODUCTION_BUILD
                D.Err("Trying to release a particle of type that was not acquired! Could be a mismatch between acquire and release: {0}", prefabName);
                #endif
            }
        }

        public static void Reset() {
            activeCount = 0;
            countsByType.Clear();
        }

        public static bool IsVFXDisabledByTunable(string vfxName) {
            #if TEST_BUDGET_CUTS
            if (Rng.UChance(0.5f)) {
                return true;
            }
            #endif
            if (vfxName == null) {
                D.Err("Checking particle budget for null vfx name! This should not happen.");
                return true;
            }
            if (vfxDisabledByTunableCache.TryGetValue(vfxName, out var cached)) {
                return cached;
            }
            var key = Hashes.S($"DisableVFX_{vfxName}");
            if (!Tunable.TryBool(key, out var disabled)) {
                D.Err("Can't find tunable vfx for vfxName: {0}", vfxName);
            }
            // disabled = true;
            vfxDisabledByTunableCache[vfxName] = disabled;
            return disabled;
        }
    }

    public sealed class ParticleBudgetDebugOutput : IDebugOutputProvider {
        public string DebugPanelTitle => "Particle Budget";

        public bool EveryFrame() {
            return false;
        }

        public bool OnTick(StringBuilder sb) {
            sb.Clear();
            sb.AppendFormat("{0}: {1} / {2}",
                Texts.Accented("Active / Budget"),
                ParticleBudget.ActiveCount >= ParticleBudget.MaxBudget
                    ? Texts.Red(ParticleBudget.ActiveCount.ToString())
                    : Texts.Green(ParticleBudget.ActiveCount.ToString()),
                Texts.Blue(ParticleBudget.MaxBudget.ToString()));
            sb.AppendFormat("\n{0}: {1}",
                Texts.Accented("Per Type Budget"),
                Texts.Blue(ParticleBudget.MaxBudgetPerType.ToString()));
            sb.AppendFormat("\n\n{0}:", Texts.Accented("By Type"));
            foreach (var kvp in ParticleBudget.CountsByType) {
                sb.AppendFormat("\n  {0}: {1}",
                    Texts.Blue(kvp.Key),
                    kvp.Value);
            }
            return true;
        }

    }
}
