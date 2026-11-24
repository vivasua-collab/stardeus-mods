using System;
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Hints;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using Priority_Queue;
using UnityEngine;

namespace Game.Systems {
    public sealed class HintSys : GameSystem, ISaveable {
        public const string SysId = "HintSys";
        public override string Id => SysId;


        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new HintSys());
        }

        public void TrackAction(string hintId) {
            if (!Ready.Gen) { return; }
            if (A.ShowHints) {
                Hint.Find(hintId).TrackAction();
            }
        }
        public void MarkNextActionAsAccidental(string hintId) {
            if (!Ready.Gen) { return; }
            if (A.ShowHints) {
                Hint.Find(hintId).ReduceActionCount();
            }
        }
        public void Deactivate(string hintId) {
            Hint.Find(hintId).Deactivate();
        }
        public bool IsRelevant(string hintId) {
            return Hint.Find(hintId).IsRelevant;
        }
        public void Show(string hintId, bool optional) {
            if (!Ready.Gen) { return; }
            if (UIShowing.MainMenu) { return; }
            // never show hints in first 5 minutes of gameplay
            #if PRODUCTION_BUILD
            if (GameTimer.time < 5 * 60) { return; }
            #endif
            if (A.ShowHints) {
                Hint.Find(hintId).Show(optional);
            }
        }

        public override void Unload() {
            ResetState();
        }

        public string GetName() {
            return Id;
        }
        private ComponentData data;

        private ComponentData Data => data ??= new(0, GetName());

        public void ResetState() {
            data = null;
            foreach (var h in Hint.All) {
                h.Value.Reset();
            }
        }

        public ComponentData Save() {
            var d = Data;
            foreach (var h in Hint.All) {
                h.Value.OnSave(d);
            }
            return d;
        }

        public void Load(ComponentData data) {
            this.data = data;
            foreach (var h in Hint.All) {
                h.Value.OnLoad(data);
            }
        }

        protected override void OnInitialize() {
        }
    }
}
