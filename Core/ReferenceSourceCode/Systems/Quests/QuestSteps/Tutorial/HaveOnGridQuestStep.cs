using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Crafting;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class HaveOnGridQuestStep : BaseQuestStep, ITickListener,
            IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HaveOnGridQuestStep(s, c, l));
        }
        public const string CheckId = "HaveOnGrid";
        private readonly Def def;
        private readonly int buildCount;
        private bool needsCheckOnTick;
        private bool requireUnconfigured;
        private bool isSubscribed;
        public HaveOnGridQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var cfg = step.Def.Config;
            var buildDef = cfg.GetString(PropertyIdH.Definition);
            buildCount = cfg.GetInt(PropertyIdH.Count, 1);
            requireUnconfigured = cfg.GetBool(PropertyIdH.RequireUnconfigured, false);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Throw("Misconfigured step, no def by id: {0} | {1}", buildDef, step);
            }
            var key = step.Def.GetTitleKey(step.Data);
            var defLink = def.CodexLink;
            if (buildCount > 1) {
                defLink = $"{defLink} (x{buildCount})";
            }
            if (key != null) {
                step.Title = key.T(defLink);
            } else {
                step.Title = T.QuestStepHaveOnGridOne_V1.F(defLink);
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

        // Watching individual connections was not enough: the device gets onto the CPU grid
        // whenever the grids merge anywhere, and that merge does not have to name it. A
        // connector reaching the stasis pod's own connector puts the array on the grid without
        // either endpoint being the StasisArray, and the step used to sit there forever.
        // ElectricitySys sends this after RebuildGrids, so IsReachable is already current;
        // the scan itself is left to the tick so a completing step cannot unwind back
        // through the rebuild.
        private void OnGridChanged() {
            needsCheckOnTick = true;
        }

        private bool IsConfigured(Tile tile) {
            if (tile.TryGetComponent<RefillableStorageComp>(out var refillable)) {
                if (refillable.IsConfigured) {
                    return true;
                }
            }
            if (tile.TryGetComponent<MultiCrafterComp>(out var crafter)) {
                if (!Arrays.IsEmpty(crafter.Orders)) {
                    return true;
                }
            }
            // TODO add more checks as needed
            return false;
        }

        public bool CheckCompleted() {
            int count = 0;
            foreach (var conn in A.Grid.NodeComponents.Values) {
                if (conn.Node.IsReachable && conn.Tile.Definition.IdH == def.IdH) {
                    if (requireUnconfigured && IsConfigured(conn.Tile)) {
                        continue;
                    }
                    count++;
                    // D.Warn("Connected {0}/{1} {2}", count, buildCount, def.NameT);
                    if (count >= buildCount) {
                        return true;
                    }
                }
            }
            return false;
        }


        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                // Activating mid-quest (e.g. via ActivateWhen) only starts listening for
                // future signals - re-check now in case the condition is already met.
                needsCheckOnTick = true;
                Subscribe();
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.ElectricityGridChanged.AddListener(OnGridChanged);
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.ElectricityGridChanged.RemoveListener(OnGridChanged);
            }
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
