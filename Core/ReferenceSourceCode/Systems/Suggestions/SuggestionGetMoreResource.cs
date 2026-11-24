using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Crafting;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Suggestions {
    public class SuggestionGetMoreResource : Suggestion, ISaveableChild {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionGetMoreResource());
        }

        public override Action<GameState> UIAction => (s) => {
            s.UIQuery.ToggleInventoryPanel.Ask();
        };
        public override string UIActionText => T.OpenResourcesOverlay;
        public const string SuggId = "GetMoreResource";
        public override string Id => SuggId;
        public override string WaitsForBool => QuestBoolIds.HasRadar;
        public override string IconId => lackingMaterialType.IconId;
        private long nextUpdate;
        private MatType lackingMaterialType;
        private int targetCount;
        public override bool IsRecurring => true;
        public override string Title => T.GoalGetmoreresource_V1.F(lackingMaterialType.NameT);

        public override string Description => T.GoalGetmoreresourceDesc_V1.F(lackingMaterialType.Def.CodexLink);
        private readonly static Dictionary<int, int> lowCounts = new() {
            { MatIdH.SteelPlate, 10 },
            { MatIdH.Water, 10 },
            { MatIdH.TitaniumPlate, 10 },
            { MatIdH.CopperWire, 10 },
            { MatIdH.Transistor, 10 },
            { MatIdH.Gears, 10 },
        };

        private readonly Dictionary<MatType, int> remaining = new();
        public override bool IsAvailable(GameState state) {
            var ticks = state.Clock.Ticks;
            if (ticks < Consts.TicksPer7Days) { return false; }
            if (ticks > Consts.TicksPer2Months) { return false; }
            if (ticks < nextUpdate) { return false; }
            nextUpdate = ticks + Consts.TicksPerHour * 2;
            state.Sys.Inventory.LoadRemainingMaterials(remaining);
            foreach (var mat in lowCounts) {
                if (!CheckMatCount(mat.Value, mat.Key)) {
                    if (!IsProducingMaterial(state, lackingMaterialType)) { return true; }
                }
            }
            return false;
        }

        private bool CheckMatCount(int wantedCnt, int type) {
            var mat = MatType.FastGet(type);
            if (!remaining.TryGetValue(mat, out var cnt)) {
                cnt = 0;
            }
            if (cnt < wantedCnt) {
                lackingMaterialType = mat;
                targetCount = wantedCnt + 5;
                return false;
            }
            return true;
        }

        private static bool IsProducingMaterial(GameState state, MatType mat) {
            var recipes = Craftable.FindForDef(mat.Def);
            if (recipes == null) { return false; }
            var allOrders = state.Query.GetAllCraftingOrders.Ask();
            if (allOrders == null) { return false; }
            for (int i = 0; i < recipes.Count; i++) {
                if (!allOrders.TryGetValue(recipes[i], out var orders)) { continue; }
                for (int j = 0; j < orders.Count; j++) {
                    var order = orders[j];
                    if (order.State == CraftingOrderState.Paused) { continue; }
                    var demand = order.Demand;
                    if (demand == null) { continue; }
                    if (demand.Type == CraftingDemandType.One
                            && demand.AmountProduced > 0) { continue; }
                    if (demand.Type == CraftingDemandType.Custom
                            && demand.AmountProduced >= demand.AmountWanted) { continue; }
                    return true;
                }
            }
            return false;
        }

        public override bool IsComplete(GameState state) {
            var ticks = state.Clock.Ticks;
            if (ticks < nextUpdate) { return false; }
            if (lackingMaterialType == null) {
                D.Err("Lacking material type is null in suggest get more resource!");
                return true;
            }
            nextUpdate = ticks + Consts.TicksPer15Minutes;
            return state.Sys.Inventory.HasEnough(lackingMaterialType, targetCount);
        }

        public override float Score(GameState state) {
            return float.MaxValue - 5000;
        }

        public void OnSave(ComponentData data) {
            data.SetString(DataKey("MatType"), lackingMaterialType?.Id);
        }

        public void OnLoad(ComponentData data) {
            var matId = data.GetString(DataKey("MatType"), null);
            if (matId != null) {
                lackingMaterialType = MatType.Get(matId);
            } else {
                lackingMaterialType = null;
            }
            remaining.Clear();
            nextUpdate = 0;
        }
    }
}
