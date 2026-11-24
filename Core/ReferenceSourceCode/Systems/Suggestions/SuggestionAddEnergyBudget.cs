using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.UI;

namespace Game.Systems.Suggestions {
    public class SuggestionAddEnergyBudget : Suggestion, ISaveableChild {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionAddEnergyBudget());
        }

        public override Action<GameState> UIAction => (s) => {
            UIUtils.CloseFullScreenUI();
            s.UIQuery.ToggleElectricity.Ask(true);
        };
        public override string UIActionText => T.OpenEnergyOverlay;
        public const string SuggId = "AddEnergyBudget";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CEnergy;
        public override bool IsRecurring => false;

        public override string Description => T.GoalAddenergybudgetDesc_V1.F(
            Texts.Accented(Units.Electricity(lastBudget)));

        private float lastBudget;

        public override bool IsAvailable(GameState state) {
            var sys = state.Sys.Electricity;
            if (sys.GridsCount == 0) { return false; }
            // Make this suggestion not show up during magnetic storm
            var affected = state.BB.Get(BBKeyH.MagneticStormAffectedId, 0);
            if (affected != 0) {
                return false;
            }
            for (int i = 0; i < sys.GridsCount; i++) {
                var grid = sys.GetGrid(i);
                if (!grid.HasShipComputer) { continue; }
                if (grid.MaxOutputWithoutBat < 15f) { continue; }
                var ratio = grid.Consumption / grid.MaxOutputWithoutBat;
                if (ratio > 0.8f) {
                    // Let's not show this if actual output is lower (dirty panels)
                    lastBudget = grid.MaxOutputWithoutBat;
                    return true;
                }
            }
            return false;
        }

        public override bool IsComplete(GameState state) {
            var sys = state.Sys.Electricity;
            if (sys.GridsCount == 0) { return false; }
            for (int i = 0; i < sys.GridsCount; i++) {
                var grid = sys.GetGrid(i);
                if (!grid.HasShipComputer) { continue; }
                if (grid.MaxOutputWithoutBat < 15f) { continue; }
                if (grid.MaxOutputWithoutBat > lastBudget * 1.2f) {
                    return true;
                }
            }
            return false;
        }

        public override float Score(GameState state) {
            return float.MaxValue - 5000;
        }

        public void OnSave(ComponentData data) {
            data.SetFloat(DataKey("LastBudget"), lastBudget);
        }

        public void OnLoad(ComponentData data) {
            lastBudget = data.GetFloat(DataKey("LastBudget"), 0);
        }
    }
}
