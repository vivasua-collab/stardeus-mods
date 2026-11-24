using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.Systems.Ships;
using Game.Systems.UIUX.Codex.Pages;
using Game.Input;
using Game.Systems.UIUX;

namespace Game.Systems.Suggestions {
public class SuggestionChangeShipClass : Suggestion, ISaveableChild {
        [SelfInit]
        private static void AutoRegister() {
            Register(new SuggestionChangeShipClass());
        }

        private static readonly List<ShipClassDef> betterClasses = new();
        // Classes the player chose to ignore - suggestion only comes back for others
        private readonly HashSet<string> ignoredClassIds = new();
        private string classIdAtAdd;

        public IReadOnlyCollection<string> IgnoredClassIds => ignoredClassIds;

        public override Action<GameState> UIAction => (s) => {
            s.Sys.NPCShip.ShowShipClassChoice(false);
        };

        public override string UIActionText {
            get {
                return T.VerbUpgrade;
            }
        }
        public const string SuggId = "ChangeShipClass";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CSpaceShip;
        public override bool IsRecurring => true;
        public override bool CanIgnore => true;
        public override string Title => $"{T.NounShipClass}: {Texts.Accented(T.UpgradeAvailable)}";
        public override string Description => T.ShipClassUpgradeAvailableDesc_V3.F(
            Texts.LinkToCodexManual(T.NounShipClass, ShipClassPage.PageId),
            Texts.LinkToShipComputerApp(ShipComputerApp_ShipConfig.AppId, UIId.ShipConfig),
            Texts.Accented(T.UpgradeShipClass, false));

        public override bool IsAvailable(GameState state) {
            return false;
        }

        public override void OnAdded(GameState state) {
            classIdAtAdd = state.Universe.Player.Ship.Class.Id;
        }

        public override bool IsComplete(GameState state) {
            var cl = state.Universe.Player.Ship.Class;
            var classAtAdd = classIdAtAdd == null ? null : ShipClassDef.Get(classIdAtAdd);
            if (classAtAdd == null) {
                classIdAtAdd = cl.Id;
                return false;
            }
            return cl.MaxClassPoints > classAtAdd.MaxClassPoints;
        }

        public override bool ShouldBeDismissed(GameState state) {
            return !ShipClassDef.IsBetterClassAvailableForPlayer(state, ignoredClassIds);
        }

        public override void OnIgnore(GameState state) {
            betterClasses.Clear();
            ShipClassDef.CollectBetterClassesForPlayer(state, betterClasses);
            for (int i = 0; i < betterClasses.Count; i++) {
                ignoredClassIds.Add(betterClasses[i].Id);
            }
            betterClasses.Clear();
        }

        protected override void OnReset() {
            ignoredClassIds.Clear();
            classIdAtAdd = null;
        }

        public override float Score(GameState state) {
            return 0;
        }

        public void OnSave(ComponentData data) {
            string[] ids = null;
            if (ignoredClassIds.Count > 0) {
                ids = new string[ignoredClassIds.Count];
                ignoredClassIds.CopyTo(ids);
            }
            data.SetStringSet(DataKey("IgnoredClasses"), ids);
            data.SetString(DataKey("ClassAtAdd"), classIdAtAdd);
        }

        public void OnLoad(ComponentData data) {
            ignoredClassIds.Clear();
            var ids = data.GetStringSet(DataKey("IgnoredClasses"), null);
            if (ids != null) {
                for (int i = 0; i < ids.Length; i++) {
                    ignoredClassIds.Add(ids[i]);
                }
            }
            classIdAtAdd = data.GetString(DataKey("ClassAtAdd"), null);
        }
    }
}
