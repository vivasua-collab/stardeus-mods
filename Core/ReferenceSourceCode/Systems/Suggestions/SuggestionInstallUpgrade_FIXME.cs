#if false
// disabled due to translation incompatibility with the new UI
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Components.Upgrades;

namespace Game.Systems.Suggestions {
    public class SuggestionInstallUpgrade : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionInstallUpgrade());
        }
        public const string SuggId = "InstallUpgrade";
        public override string Id => SuggId;
        public override string Description {
            get {
                return T.GoalInstallupgradeDesc_V1.F(T.Upgrades);
            }
        }
        public override string IconId => Constants.GraphicsId.ObjUpgradesReactorEfficiencyUpgrade;

        public override bool CheckDependencies(HashSet<string> completedGoals) {
            return completedGoals.Contains(SuggestionStartWreckedScenario.SuggId);
        }
        public override bool IsAvailable(GameState state) {
            // Optimization
            if (state.Clock.Ticks > Consts.TicksPer2Weeks) {
                return false;
            }
            // not too early
            if (state.Clock.Ticks < Consts.TicksPer3Days) {
                return false;
            }
            var reactor = state.Components.FindFirstMatching<MatterReactorComp>(
                    m => m.Tile.IsConstructed && m.Tile.ENode.IsReachable);
            if (reactor == null) { return false; }
            var upgrades = state.Sys.Upg.AvailableFor(reactor.Entity);
            if (upgrades == null) {
                return false;
            }
            foreach (var upg in upgrades) {
                if (upg is ReactorEfficiencyUpgrade) {
                    return true;
                }
            }
            return !Arrays.IsEmpty(upgrades);
        }

        public override bool IsComplete(GameState state) {
            var reactor = state.Components.FindFirstMatching<MatterReactorComp>(
                    m => m.Tile.IsConstructed && m.UpgradeEfficiency > 1.1f);
            return reactor != null;
        }

        public override float Score(GameState state) {
            return int.MaxValue / 2;
        }
    }
}
#endif
