using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class BuildTileQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new BuildTileQuestStep(s, c, l));
        }
        public const string CheckId = "BuildTile";
        private readonly Def def;
        private readonly int buildCount;
        private bool isSubscribed;
        private bool countExisting;
        public BuildTileQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var buildDef = cfg.GetString(PropertyIdH.Definition);
            buildCount = cfg.GetInt(PropertyIdH.Count, 1);
            countExisting = cfg.GetBool(PropertyIdH.CountExisting, false);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Err("Def is null for quest step: {0} -> {1}", step, buildDef);
            }
            if (buildCount < 1) {
                D.Err("Quest build step has count < 1 in config: {0} -> {1}",
                    step, buildCount);
                buildCount = 1;
            }
            var key = step.Def.GetTitleKey(step.Data);
            var defLink = def.CodexLink;
            if (buildCount > 1) {
                defLink = $"{defLink} (x{buildCount})";
            }
            if (key != null) {
                step.Title = key.T(defLink);
            } else {
                step.Title = T.QuestStepBuildOne_V1.F(defLink);
            }
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public bool CheckCompleted() {
            int count = 0;
            if (def.HasComponent(ComponentIdH.ElectricNode)) {
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
                            if (++count >= buildCount) {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private void OnConstructionCompleted(Tile tile) {
            if (tile.Definition == def) {
                if (buildCount == 1) {
                    step.SetState(QuestStepState.Completed);
                } else {
                    if (CheckCompleted()) {
                        step.SetState(QuestStepState.Completed);
                    }
                }
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (countExisting && CheckCompleted()) {
                    step.SetState(QuestStepState.Completed);
                } else {
                    Subscribe();
                }
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            }
        }


        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.ConstructionCompleted.RemoveListener(OnConstructionCompleted);
            }
        }
    }
}
