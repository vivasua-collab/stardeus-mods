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
using Mono.Cecil;

namespace Game.Systems.Suggestions {
    public class SuggestionDisposeOfObjs : Suggestion, ISaveableChild {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionDisposeOfObjs());
        }
        public const string SuggId = "DisposeOfObjs";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CKnife;
        private long nextUpdate;
        private Def targetDef;
        public override bool IsRecurring => true;
        public override string Title => T.GoalDisposeOfObjs_V1.F(targetDef.NameT);
        public override string Description => T.GoalDisposeOfObjsDesc_V2.F(
            targetDef.CodexLink, T.VerbDispose);
        private readonly static Dictionary<int, int> targetThresholds = new() {
            { DefIdsH.ObjPlantsArtilleryFungus, 10 }
        };

        /// <inheritdoc/>
        public override bool IsAvailable(GameState state) {
            var ticks = state.Clock.Ticks;
            if (ticks < nextUpdate) {
                return false;
            }
            nextUpdate = ticks + Consts.TicksPer3Hours;
            foreach (var th in targetThresholds) {
                var def = The.Defs.FastGet(th.Key);
                if (state.Objs.CountByDef(def) >= th.Value) {
                    targetDef = def;
                    return true;
                }
            }
            return false;
        }

        public override bool IsComplete(GameState state) {
            var ticks = state.Clock.Ticks;
            if (ticks < nextUpdate) { return false; }
            if (targetDef == null) {
                D.Err("Target def null in suggest dispose of obj!");
                return true;
            }
            nextUpdate = ticks + Consts.TicksPer15Minutes;
            return state.Objs.CountByDef(targetDef) == 0;
        }

        public override float Score(GameState state) {
            return float.MaxValue - 2000;
        }

        public void OnSave(ComponentData data) {
            data.SetString(DataKey("TargetDef"), targetDef?.Id);
        }

        public void OnLoad(ComponentData data) {
            var id = data.GetString(DataKey("TargetDef"), null);
            if (id != null) {
                targetDef = The.Defs.Get(id);
            } else {
                targetDef = null;
            }
            nextUpdate = 0;
        }
    }
}
