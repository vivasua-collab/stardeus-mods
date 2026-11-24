using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Suggestions {
    public class SuggestionFindResource : Suggestion, ISaveableChild {
        [SelfInit]
        private static void AutoRegister() {
            Register(new SuggestionFindResource(1, 2, DefIdsH.ResearchScanningRadar));
            Register(new SuggestionFindResource(2, 4, DefIdsH.ResearchScanningSystem));
            Register(new SuggestionFindResource(4, int.MaxValue, DefIdsH.ResearchScanningExtended));
        }

        public SuggestionFindResource(int minRarity, int maxRarityExclusive, int researchDefIdH) {
            this.minRarity = minRarity;
            this.maxRarity = maxRarityExclusive;
            this.researchDefIdH = researchDefIdH;
            this.suggId = $"FindResource_{minRarity}";
        }
        private readonly string suggId;
        private readonly int minRarity;
        private readonly int maxRarity;
        private readonly int researchDefIdH;
        public override Action<GameState> UIAction => (s) => {
            if (s.Research.IsDefUnlocked(researchDef)) {
                s.UIQuery.ShowStarmap.Ask(true);
            } else {
                s.Sys.Research.ShowResearch(researchDefIdH);
            }
        };

        public override string UIActionText {
            get {
                if (The.R?.State?.Research.IsDefUnlocked(researchDef) == true) {
                    return T.OpenStarmap;
                } else {
                    return T.OpenResearch;
                }
            }
        }
        public override string Id => suggId;
        // Finding a resource means scanning and expeditions, which the radar quest opens
        public override string WaitsForBool => QuestBoolIds.HasRadar;
        public override string IconId => lackingMaterialType.IconId;
        private long nextUpdate;
        private MatType lackingMaterialType;
        public override bool IsRecurring => false;
        public override string Title => T.FindResource_V1.F(lackingMaterialType.NameT);
        public override string Description => T.UnlockScanningTip_V4.F(
            lackingMaterialType.Def.CodexLink,
            The.R.State.Research.TreePathFor(researchDef),
            Texts.LinkToUI(T.Starmap, UIId.Starmap),
            Texts.Accented(T.ResearchScanning));

        private readonly Dictionary<MatType, int> remaining = new();
        private Def _researchDef;
        private Def researchDef => _researchDef ??= The.Defs.FastGet(researchDefIdH);
        public override bool IsAvailable(GameState state) {
            var ticks = state.Clock.Ticks;
            if (ticks < nextUpdate) { return false; }
            nextUpdate = ticks + Consts.TicksPerHour;

            var cnt = state.Query.GetMaterialDeficits.Ask(Caches.IntList);
            if (cnt == 0) { return false; }
            foreach (var mat in MatType.Known.Values) {
                if (mat.Deposit == null) { continue; }
                if (mat.MatRarity < minRarity || mat.MatRarity >= maxRarity) { continue; }
                if (Caches.IntList.Contains(mat.IdH)) {
                    lackingMaterialType = mat;
                    return true;
                }
            }
            return false;
        }

        public override bool IsComplete(GameState state) {
            var ticks = state.Clock.Ticks;
            if (ticks < nextUpdate) { return false; }
            nextUpdate = ticks + Consts.TicksPer15Minutes;
            if (!state.Research.IsDefUnlocked(researchDef)) {
                return false;
            } else {
                var cnt = state.Query.GetMaterialDeficits.Ask(Caches.IntList);
                if (cnt == 0) { return true; }
                if (lackingMaterialType == null) {
                    D.Err("Lacking material type is null in suggest get more resource!");
                    return true;
                }
                if (!Caches.IntList.Contains(lackingMaterialType.IdH)) {
                    return true;
                }

                var scan = state.Sys.Scan;
                return scan.Mode.Value == ScanMode.Mat
                    && scan.MatTarget.Value == lackingMaterialType
                    && scan.ScanProgress > 0.999f
                    && scan.FoundSO != null;
            }
        }

        public override float Score(GameState state) {
            return float.MaxValue - 3000;
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
