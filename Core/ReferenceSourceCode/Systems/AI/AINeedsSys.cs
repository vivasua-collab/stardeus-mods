#if UNITY_EDITOR
// #define DEBUG_NEEDS
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.Creatures;
using Game.Tools;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.Pool;

namespace Game.Systems.AI {
    public sealed class AINeedsSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new AINeedsSys());
        }

        public const string SysId = "AINeedsSys";
        public override string Id => SysId;
        private readonly List<NeedState> allNeeds = new();
        private readonly List<NeedState> tempNeeds = new();
        private readonly HashSet<(uint, int)> agentNeeds = new();


        public override void Unload() {
            allNeeds.Clear();
            tempNeeds.Clear();
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.NeedsChanged.AddListener(OnNeedsChanged);
            S.Sig.AIRemoveAgent.AddListener(OnRemoveAgent);
        }

        private void OnRemoveAgent(AIAgentComp agent) {
            var data = agent.BeingData;
            for (int i = allNeeds.Count - 1; i >= 0; i--) {
                NeedState n = allNeeds[i];
                if (n.Data == data) {
                    var h = (data.Id, n.Need.IdH);
                    if (!agentNeeds.Remove(h)) {
                        D.Err("Could not find need in agentNeeds for removal: {0}->{1}",
                            n, agent);
                    }
                    allNeeds.RemoveAt(i);
                }
            }
        }

        private void OnNeedsChanged(Being b) {
            if (b?.Data == null) { return; }
            var needSources = b.Data.NeedSources;
            var agent = b.AIAgent;
            var existingNeeds = Caches.IntSet;
            existingNeeds.Clear();
            if (b.IsAlive) {
                for (int i = needSources.Count - 1; i >= 0; i--) {
                    var src = needSources[i];
                    if (src.Need == null) {
                        D.Err("Need source has null need! {0} -> {1}", src, b);
                        continue;
                    }
                    if (existingNeeds.Add(src.Need.IdH)) {
                        OnAddNeed(b.Data, src.Need);
                    }
                }
            }

            for (int i = allNeeds.Count - 1; i >= 0; i--) {
                var n = allNeeds[i];
                if (n.Data == b.Data && !existingNeeds.Contains(n.Need.IdH)) {
                    // we found a need that does not exist anymore
                    var agentNeed = (b.Data.Id, n.Need.IdH);
                    if (!agentNeeds.Remove(agentNeed)) {
                        D.Err("Found agent need {0}->{1} but no entry in agentNeeds when removing a need!",
                            agent, n.Need);
                    }
                    allNeeds.RemoveAt(i);
                    // Let the need tear down whatever it set up (e.g. MentalHealth drops the
                    // sanity object when the brain that granted it is gone).
                    n.Need.OnRemoveNeed(n.Data);
                }
            }
            ValidateNeeds();
        }

        private void OnAddNeed(BeingData data, AINeed need) {
            if (data.SkipSimulate) {
                D.Err("Trying to add a need for an inactive being: {0}: {1}",
                    data, need);
                return;
            }
            var h = (data.Id, need.IdH);
            if (!agentNeeds.Add(h)) {
                return;
            }
            #if DEBUG_NEEDS
            D.Warn("Creating need: {0} -> {1}", need, agent);
            #endif

            allNeeds.Add(new NeedState() {
                Need = need,
                Data = data,
            });
            need.InitializeFor(data);
            ValidateNeeds();
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.CheckNeedActive.SetResponder(CheckNeedActive);
            ValidateNeeds();
        }

        private bool CheckNeedActive(BeingData data, int needId) {
            return agentNeeds.Contains((data.Id, needId));
        }

        private readonly ErrorThrottle tickErrors = new();

        private void OnTick(long ticks) {
            tempNeeds.Clear();
            NeedState n = default;
            // build temp needs list
            for (int i = allNeeds.Count - 1; i >= 0; i--) {
                n = allNeeds[i];
                tempNeeds.Add(n);
            }
            // tick all temp needs, each in its own try: one need that threw on every tick used
            // to stop the needs of every being after it (hunger, sleep, oxygen froze)
            for (int i = tempNeeds.Count - 1; i >= 0; i--) {
                n = tempNeeds[i];
                // #if !PRODUCTION_BUILD
                // if (n.Data.Being.IsDead) {
                //     D.Err("Ticking need for dead being: {0}", n);
                // }
                // #endif
                try {
                    n.Need.Tick(S, n.Data, ticks);
                } catch (System.Exception ex) {
                    if (tickErrors.ShouldLog(n.Data, out var count)) {
                        D.LogEx(ex, "Caught exception while ticking need: {0} -> {1} ({2} times)",
                            n, n.Data, count);
                    }
                }
            }
            // the throttle holds its keys, so gone beings are let go daily
            if (ticks % Consts.TicksPerDay == 0) {
                tickErrors.Clear();
            }
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                CheckAllNeedsExist();
            }
            #endif
        }

        private void CheckAllNeedsExist() {
            var beings = HashSetPool<BeingData>.Get();
            beings.Clear();
            for (int i1 = allNeeds.Count - 1; i1 >= 0; i1--) {
                NeedState n = allNeeds[i1];
                beings.Add(n.Data);
            }

            for (int i = S.Beings.Alive.Count - 1; i >= 0; i--) {
                var b = S.Beings.Alive[i].Data;
                if (b.HasStateFlag(BeingState.InDevice) || b.Body.IsIncapacitated) {
                    continue;
                }
                if (!beings.Remove(b)) {
                    D.Err("Being has no needs registered: {0}", b);
                }
            }
            beings.Clear();
            HashSetPool<BeingData>.Release(beings);
        }

        private void ValidateNeeds() {
            if (allNeeds.Count == agentNeeds.Count) { return; }
            D.Err("All needs count does not match agent needs count! {0} != {1}",
                allNeeds.Count, agentNeeds.Count);

            // first try to remove needs that do not belong
            for (int i = allNeeds.Count - 1; i >= 0; i--) {
                NeedState need = allNeeds[i];
                var h = (need.Data.Id, need.Need.IdH);
                if (!agentNeeds.Contains(h)) {
                    D.Err("Found need that does not exist in agentNeeds: {0}", need);
                    allNeeds.RemoveAt(i);
                }
            }

            if (allNeeds.Count == agentNeeds.Count) { return; }
            D.Err("AgentNeeds has entries that do not have corresponding allNeeds! This is BAD!");
        }
    }
}
