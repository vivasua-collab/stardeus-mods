using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Stats {
    public sealed class StatsSys : GameSystem {
        public const string SysId = "StatsSys";
        public override string Id => SysId;
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new StatsSys());
        }

        private readonly List<StatsContainer> all = new();

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.ActivateStats.AddListener(OnToggleStats);
        }

        private void OnToggleStats(StatsContainer stats, bool on) {
            if (on) {
                all.Add(stats);
                UpdateStats(stats.AllStats);
            } else {
                all.Remove(stats);
            }
        }

        private void OnLoadState(GameState s) {
            s.Clock.OnTickEarly.AddListener(OnTickEarly);
            OnTickEarly(G.Ticks);
        }

        private void OnTickEarly(long ticks) {
            for (int i = all.Count - 1; i >= 0; i--) {
                UpdateStats(all[i].AllStats);
            }
        }

        private void UpdateStats(Stat[] stats) {
            for (int i = stats.Length - 1; i >= 0; i--) {
                stats[i].Apply();
            }
        }
    }
}
