using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Completes when the device specified by a quest variable is built on the ship.
    /// For electric devices (HasComponent ElectricNode): requires CPU-reachable grid connection.
    /// For non-electric devices: requires construction.
    /// Config: Var_Definition (string, default "Device"), Count (int, default 1).
    /// </summary>
    public sealed class BuildAndMakeFunctionalQuestStep : BaseQuestStep,
            ITickListener, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new BuildAndMakeFunctionalQuestStep(s, c, l));
        }

        public const string CheckId = "BuildAndMakeFunctional";

        private readonly Def def;
        private readonly int buildCount;
        private readonly bool isElectric;
        private bool isSubscribed;
        private bool needsCheckOnTick;

        public BuildAndMakeFunctionalQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            buildCount = config.GetInt(PropertyIdH.Count, 1);
            def = GetQuestVarValByKey<Def>(PropertyIdH.Var_Definition);
            if (def == null) {
                D.Throw("BuildAndMakeFunctional: device def is null | {0}", step);
            }
            isElectric = def.HasComponent(ComponentIdH.ElectricNode);

            var defLink = def.CodexLink;
            if (buildCount > 1) {
                defLink = $"{defLink} (x{buildCount})";
            }
            var key = step.Def.GetTitleKey(step.Data);
            if (key != null) {
                step.Title = key.T(defLink);
            } else if (isElectric) {
                step.Title = T.QuestStepHaveOnGridOne_V1.F(defLink);
            } else {
                step.Title = T.QuestStepBuildOne_V1.F(defLink);
            }
            if (isLoading && step.IsActive) {
                needsCheckOnTick = true;
                Subscribe();
            }
        }

        public void Tick(long ticks) {
            if (needsCheckOnTick && Ready.Energy) {
                needsCheckOnTick = false;
                if (step.IsActive && CheckCompleted()) {
                    CompleteStep();
                }
            }
        }

        public bool CheckCompleted() {
            int count = 0;
            if (isElectric) {
                foreach (var conn in A.Grid.NodeComponents.Values) {
                    if (conn.Node.IsReachable && conn.Tile.Definition.IdH == def.IdH) {
                        if (++count >= buildCount) {
                            return true;
                        }
                    }
                }
            } else {
                if (S.AllByDef(def, out var items)) {
                    foreach (Tile item in items.Values) {
                        if (item.IsConstructed) {
                            // ignore devices outside the build zone (derelicts)
                            if (!EntityUtils.IsWithinMargin(item.PosIdx, false, false)) {
                                continue;
                            }
                            if (++count >= buildCount) {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private void OnNodeConnected(int id1, int id2, bool connected) {
            if (!connected) { return; }
            var node1 = S.Components.Find<ElectricNodeComp>(id1);
            var node2 = S.Components.Find<ElectricNodeComp>(id2);
            if (node1 == null || node2 == null ||
                    !node1.Entity.IsActive || !node2.Entity.IsActive) {
                return;
            }
            if (!node1.Node.IsReachable && !node2.Node.IsReachable) { return; }
            if (node1.Entity.Definition != def && node2.Entity.Definition != def) { return; }
            if (buildCount == 1) {
                CompleteStep();
            } else {
                needsCheckOnTick = true;
            }
        }

        private void OnConstructionCompleted(Tile tile) {
            if (tile.Definition != def) { return; }
            if (buildCount == 1) {
                CompleteStep();
            } else if (CheckCompleted()) {
                CompleteStep();
            }
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            if (isElectric) {
                S.Sig.EnergyConnectionChanged.AddListener(OnNodeConnected);
            } else {
                S.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                // Activating mid-quest (e.g. via ActivateWhen) only starts listening for
                // future signals - re-check now in case the condition is already met.
                needsCheckOnTick = true;
                Subscribe();
            }
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            if (isElectric) {
                S.Sig?.EnergyConnectionChanged.RemoveListener(OnNodeConnected);
            } else {
                S.Sig?.ConstructionCompleted.RemoveListener(OnConstructionCompleted);
            }
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
